using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rules;
using Clockwork.Runtime.Policy;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// The member-aware half of <see cref="RewriteOperationKind.SubstituteType"/>: where
/// <see cref="TypeReferenceRewritingPass"/> only replaces type-reference <em>operands</em>, this pass
/// projects the same substitutions onto method signatures, field types, local-variable types, and the declaring types of
/// method and field references, and rewrites the awaiter-source calls the C# compiler emits
/// (<c>task.GetAwaiter()</c>, <c>task.ConfigureAwait(bool)</c>, and
/// <c>task.ConfigureAwait(ConfigureAwaitOptions)</c>) into a <c>newobj</c> of the controlled
/// awaitable/awaiter. Together these let a compiler-generated <c>async</c> state machine be
/// retargeted from the BCL builder/awaiter types onto Clockwork's controlled equivalents, so every
/// continuation is scheduled through the simulation coordinator instead of the thread pool.
/// </summary>
/// <remarks>
/// The pass is a no-op unless the rule set declares type substitutions (so ordinary BCL rule sets are
/// unaffected), and it only touches references composed from substituted types, so non-async code in a
/// rewritten assembly is left byte-for-byte unchanged. It fails closed: a member reference whose
/// substituted signature cannot bind against its declaring type - one defined in a framework or
/// replacement assembly, which is never rewritten, or in a type of this module excluded from rewriting -
/// is reported as <see cref="RewriteDiagnosticIds.UnresolvedReplacement"/> instead of being emitted.
/// </remarks>
internal sealed class MemberSubstitutionRewritingPass : RewritePass
{
    private static readonly HashSet<string> AwaiterSourceReceivers = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
    };

    private MemberSubstitutionMapper? _mapper;
    private readonly HashSet<string> _reportedOutOfRange = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedApplicationFailures = new(StringComparer.Ordinal);
    private readonly List<PendingBinding> _pendingBindings = [];

    public MemberSubstitutionRewritingPass(RewriteSession session)
        : base(session)
    {
    }

    private MemberSubstitutionMapper Mapper => _mapper ??= BuildMapper();

    private bool IsActive => Session.Matcher.HasTypeSubstitutions && !Mapper.IsEmpty;

    /// <inheritdoc/>
    internal override void VisitType(TypeDefinition type)
    {
        base.VisitType(type);
        ReportOutOfRange(type.Fields.Select(field => field.FieldType));
        ReportOutOfRange(type.Properties.Select(property => property.PropertyType));
        ReportOutOfRange(type.Properties.SelectMany(property => property.Parameters).Select(parameter => parameter.ParameterType));
        ReportOutOfRange(type.Methods.Select(method => method.ReturnType));
        ReportOutOfRange(type.Methods.SelectMany(method => method.Parameters).Select(parameter => parameter.ParameterType));

        if (!IsActive)
        {
            return;
        }

        foreach (FieldDefinition field in type.Fields)
        {
            TypeReference? mapped = Mapper.MapType(field.FieldType);
            if (mapped is not null)
            {
                RecordType(mapped, field.FieldType);
                field.FieldType = mapped;
            }
        }

        foreach (PropertyDefinition property in type.Properties)
        {
            TypeReference? mappedProperty = Mapper.MapType(property.PropertyType);
            if (mappedProperty is not null)
            {
                property.PropertyType = mappedProperty;
            }

            foreach (ParameterDefinition parameter in property.Parameters)
            {
                TypeReference? mappedParameter = Mapper.MapType(parameter.ParameterType);
                if (mappedParameter is not null)
                {
                    parameter.ParameterType = mappedParameter;
                }
            }
        }

        // A substituted type can occur directly or inside a generic method signature, such as
        // Action<Barrier> in a compiler-generated callback. Leaving these signatures unchanged would
        // produce incompatible closure and public API references after the whole-type substitution.
        foreach (MethodDefinition method in type.Methods)
        {
            TypeReference? mappedReturn = Mapper.MapType(method.ReturnType);
            if (mappedReturn is not null)
            {
                method.ReturnType = mappedReturn;
            }

            foreach (ParameterDefinition parameter in method.Parameters)
            {
                TypeReference? mappedParameter = Mapper.MapType(parameter.ParameterType);
                if (mappedParameter is not null)
                {
                    parameter.ParameterType = mappedParameter;
                }
            }
        }
    }

    /// <inheritdoc/>
    protected override void VisitMethodBody(MethodBody body)
    {
        ReportOutOfRange(body.Variables.Select(variable => variable.VariableType));
        if (!IsActive)
        {
            return;
        }

        foreach (VariableDefinition variable in body.Variables)
        {
            TypeReference? mapped = Mapper.MapType(variable.VariableType);
            if (mapped is not null)
            {
                variable.VariableType = mapped;
                IsMethodBodyModified = true;
            }
        }
    }

    /// <inheritdoc/>
    protected override Instruction VisitInstruction(Instruction instruction)
    {
        ReportOutOfRange(instruction);

        if (!IsActive)
        {
            return instruction;
        }

        switch (instruction.Operand)
        {
            case MethodReference method:
                return VisitMethodOperand(instruction, method);

            case FieldReference field and not FieldDefinition:
                {
                    MemberMapping<FieldReference> mapping = Mapper.MapField(field);
                    if (mapping.Kind == MemberMappingKind.Mapped)
                    {
                        FieldReference mapped = mapping.Reference!;
                        instruction.Operand = mapped;
                        IsMethodBodyModified = true;
                        RecordType(mapped.FieldType, field.FieldType, instruction);
                        TrackBinding(field, mapping, instruction);
                    }
                    else if (mapping.Kind == MemberMappingKind.Unbindable)
                    {
                        ReportUnappliedMemberSubstitution(field, instruction, mapping.Reason!);
                    }

                    return instruction;
                }

            default:
                return instruction;
        }
    }

    private Instruction VisitMethodOperand(Instruction instruction, MethodReference method)
    {
        // The compiler produces an awaiter from task.GetAwaiter() / task.ConfigureAwait(bool) /
        // task.ConfigureAwait(ConfigureAwaitOptions). The controlled awaiter/awaitable is constructed
        // instead, through the constructor whose parameter types are the receiver followed by the call's
        // own, so it consumes the identical stack and pushes the controlled value, and a one-for-one
        // instruction swap to newobj keeps the surrounding IL valid.
        if ((instruction.OpCode.Code is Code.Call or Code.Callvirt)
            && (method.Name is "GetAwaiter" or "ConfigureAwait")
            && AwaiterSourceReceivers.Contains(CecilNames.NormalizedTypeFullName(method.DeclaringType)))
        {
            return TryBuildAwaiterSource(instruction, method) ?? instruction;
        }

        MemberMapping<MethodReference> mapping = Mapper.MapMethod(method);
        if (mapping.Kind == MemberMappingKind.Mapped)
        {
            RecordMethod(method, instruction);
            instruction.Operand = mapping.Reference!;
            IsMethodBodyModified = true;
            TrackBinding(method, mapping, instruction);
        }
        else if (mapping.Kind == MemberMappingKind.Unbindable)
        {
            ReportUnappliedMemberSubstitution(method, instruction, mapping.Reason!);
        }

        return instruction;
    }

    /// <inheritdoc/>
    internal override void CompleteVisit()
    {
        base.CompleteVisit();

        // Every type of the module has now been visited, so a rewritten declaring type carries its substituted
        // signatures; a reference into a type excluded from rewriting does not bind.
        foreach (PendingBinding binding in _pendingBindings)
        {
            if (BindingFailure(binding.Mapped) is string failure)
            {
                ReportUnappliedMemberSubstitution(binding.Rules, binding.Original, binding.Method, binding.Offset, failure);
            }
        }

        _pendingBindings.Clear();
    }

    // A mapped reference that keeps a declaring type of this module binds only if that type was rewritten too.
    private string? BindingFailure(MemberReference mapped)
    {
        MemberReference element = mapped is GenericInstanceMethod generic ? generic.ElementMethod : mapped;
        try
        {
            TypeDefinition? declaring = element.DeclaringType.Resolve();
            if (declaring is not null && Session.IsExcluded(declaring))
            {
                return $"maps to '{mapped.FullName}', but its declaring type '{declaring.FullName}' is excluded from " +
                    "rewriting and keeps the original signature";
            }

            return element.Resolve() is not null
                ? null
                : $"maps to '{mapped.FullName}', which does not bind to a member of '{declaring?.FullName}'";
        }
        catch (AssemblyResolutionException exception)
        {
            return $"maps to '{mapped.FullName}', which could not be resolved to check that it binds: {exception.Message}";
        }
    }

    private void TrackBinding<TMember>(MemberReference original, MemberMapping<TMember> mapping, Instruction instruction)
        where TMember : MemberReference
    {
        if (mapping.VerifyAfterPass)
        {
            _pendingBindings.Add(PendingFor(original, mapping.Reference!, instruction));
        }
    }

    // The rules and the original's name are taken now: a definition's signature may be rewritten in place later.
    private PendingBinding PendingFor(MemberReference original, MemberReference mapped, Instruction instruction) =>
        new(
            MemberRules(original),
            original.FullName,
            mapped,
            CecilNames.FullyQualifiedMethodName(Method!),
            instruction.Offset);

    private Instruction? TryBuildAwaiterSource(Instruction instruction, MethodReference method)
    {
        MethodReference? constructor = Mapper.BuildAwaiterSourceConstructor(method);
        if (constructor is null)
        {
            if (Session.Matcher.GetMatchingTypeRules(method.ReturnType).Count > 0)
            {
                ReportUnappliedMemberSubstitution(method, instruction, "could not be mapped to the replacement type");
            }

            return null;
        }

        Instruction replacement = Processor!.Create(OpCodes.Newobj, constructor);
        RecordType(constructor.DeclaringType, method.ReturnType, instruction);
        Replace(instruction, replacement);
        return replacement;
    }

    private MemberSubstitutionMapper BuildMapper() =>
        MemberSubstitutionMapper.Build(Session, (rule, error) =>
        {
            Session.AddDiagnostic(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.UnresolvedReplacement,
                $"{error} Member-aware substitution rule '{rule.Id}' could not be applied."));
            Session.AddUnresolvedReference(rule.Replacement.ToCanonicalString());
        });

    private void ReportOutOfRange(IEnumerable<TypeReference> types)
    {
        foreach (TypeReference type in types)
        {
            ReportOutOfRange(type, instruction: null);
        }
    }

    private void ReportOutOfRange(Instruction instruction)
    {
        switch (instruction.Operand)
        {
            case MethodReference method:
                ReportOutOfRange(method.DeclaringType, instruction);
                ReportOutOfRange(method.ReturnType, instruction);
                ReportOutOfRange(method.Parameters.Select(parameter => parameter.ParameterType), instruction);
                if (method is GenericInstanceMethod generic)
                {
                    ReportOutOfRange(generic.GenericArguments, instruction);
                }

                break;
            case FieldReference field:
                ReportOutOfRange(field.DeclaringType, instruction);
                ReportOutOfRange(field.FieldType, instruction);
                break;
        }
    }

    private void ReportOutOfRange(IEnumerable<TypeReference> types, Instruction instruction)
    {
        foreach (TypeReference type in types)
        {
            ReportOutOfRange(type, instruction);
        }
    }

    private void ReportOutOfRange(TypeReference type, Instruction? instruction)
    {
        foreach (RewriteRule rule in Session.Matcher.GetOutOfRangeTypeRules(type))
        {
            string containing = Method is null
                ? TypeDef?.FullName ?? Session.TargetModule.Name
                : CecilNames.FullyQualifiedMethodName(Method);
            int offset = instruction?.Offset ?? -1;
            string key = $"{rule.Id}|{containing}|{offset}";
            if (!_reportedOutOfRange.Add(key))
            {
                continue;
            }

            Session.AddDiagnostic(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.RuntimeOutOfRange,
                $"Rule '{rule.Id}' targeted type '{type.FullName}' but the configured target runtime is outside its supported range {rule.SupportedRuntimes.ToCanonicalString()}.",
                containing,
                offset));
        }
    }

    private void ReportUnappliedMemberSubstitution(MemberReference member, Instruction instruction, string failure) =>
        ReportUnappliedMemberSubstitution(
            MemberRules(member),
            member.FullName,
            CecilNames.FullyQualifiedMethodName(Method!),
            instruction.Offset,
            failure);

    private void ReportUnappliedMemberSubstitution(
        RewriteRule[] rules,
        string member,
        string containing,
        int offset,
        string failure)
    {
        foreach (RewriteRule rule in rules)
        {
            string key = $"{rule.Id}|{containing}|{offset}";
            if (!_reportedApplicationFailures.Add(key))
            {
                continue;
            }

            Session.AddDiagnostic(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.UnresolvedReplacement,
                $"Member-aware substitution rule '{rule.Id}' targets member '{member}', which {failure}.",
                containing,
                offset));
            Session.AddUnresolvedReference(rule.Replacement.ToCanonicalString());
        }
    }

    private RewriteRule[] MemberRules(MemberReference member) => member switch
    {
        GenericInstanceMethod generic => MatchingRules(
            [generic.DeclaringType, generic.ReturnType, .. generic.Parameters.Select(parameter => parameter.ParameterType), .. generic.GenericArguments]),
        MethodReference method => MatchingRules(
            [method.DeclaringType, method.ReturnType, .. method.Parameters.Select(parameter => parameter.ParameterType)]),
        FieldReference field => MatchingRules([field.DeclaringType, field.FieldType]),
        _ => [],
    };

    private RewriteRule[] MatchingRules(IEnumerable<TypeReference> types)
    {
        var rules = new Dictionary<string, RewriteRule>(StringComparer.Ordinal);
        foreach (TypeReference type in types)
        {
            foreach (RewriteRule rule in Session.Matcher.GetMatchingTypeRules(type))
            {
                rules.TryAdd(rule.Id, rule);
            }
        }

        return [.. rules.Values];
    }

    private void RecordMethod(MethodReference original, Instruction instruction)
    {
        string normalized = CecilNames.NormalizedTypeFullName(
            original is GenericInstanceMethod generic ? generic.ElementMethod.DeclaringType : original.DeclaringType);
        Record(normalized, original.DeclaringType.FullName, instruction);
    }

    private void RecordType(TypeReference mapped, TypeReference original, Instruction? instruction = null)
    {
        string normalized = CecilNames.NormalizedTypeFullName(original);
        Record(normalized, mapped.FullName, instruction);
    }

    private void Record(string normalizedOriginal, string replacement, Instruction? instruction)
    {
        string? ruleId = Mapper.RuleIdFor(normalizedOriginal);
        if (ruleId is null || Method is null)
        {
            return;
        }

        string? file = null;
        int line = -1;
        if (instruction is not null)
        {
            RewriteSession.TryGetSequencePoint(Method, instruction, out file, out line);
        }

        Session.AddTransformation(new ManifestTransformation(
            ruleId,
            RewriteOperationKind.SubstituteType,
            TransformationOutcome.Transformed,
            SimulationApiPolicy.Controlled,
            normalizedOriginal,
            replacement,
            CecilNames.FullyQualifiedMethodName(Method),
            instruction?.Offset ?? -1,
            file,
            line));
    }

    private readonly record struct PendingBinding(RewriteRule[] Rules, string Original, MemberReference Mapped, string Method, int Offset);
}
