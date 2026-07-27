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
/// projects the same substitutions onto field types, local-variable types, and the declaring types of
/// method and field references, and rewrites the two awaiter-source calls the C# compiler emits
/// (<c>task.GetAwaiter()</c> and <c>task.ConfigureAwait(bool)</c>) into a <c>newobj</c> of the
/// controlled awaitable/awaiter. Together these let a compiler-generated <c>async</c> state machine be
/// retargeted from the BCL builder/awaiter types onto Clockwork's controlled equivalents, so every
/// continuation is scheduled through the simulation coordinator instead of the thread pool.
/// </summary>
/// <remarks>
/// The pass is a no-op unless the rule set declares type substitutions (so ordinary BCL rule sets are
/// unaffected), and it only touches references composed from substituted types, so non-async code in a
/// rewritten assembly is left byte-for-byte unchanged.
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
    }

    /// <inheritdoc/>
    protected override void VisitMethodBody(MethodBody body)
    {
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
                FieldReference? mapped = Mapper.MapField(field);
                if (mapped is not null)
                {
                    instruction.Operand = mapped;
                    IsMethodBodyModified = true;
                    RecordType(mapped.FieldType, field.FieldType);
                }

                return instruction;
            }

            default:
                return instruction;
        }
    }

    private Instruction VisitMethodOperand(Instruction instruction, MethodReference method)
    {
        // The compiler produces an awaiter from task.GetAwaiter() / task.ConfigureAwait(bool). The
        // controlled awaiter/awaitable is constructed instead, which consumes the identical stack (the
        // receiver, plus the bool for ConfigureAwait) and pushes the controlled value, so a one-for-one
        // instruction swap to newobj keeps the surrounding IL valid.
        if ((instruction.OpCode.Code is Code.Call or Code.Callvirt)
            && AwaiterSourceReceivers.Contains(CecilNames.NormalizedTypeFullName(method.DeclaringType)))
        {
            if (method.Name == "GetAwaiter" && method.Parameters.Count == 0)
            {
                return TryBuildAwaiterSource(instruction, method, arity: 1) ?? instruction;
            }

            if (method.Name == "ConfigureAwait" && method.Parameters.Count == 1)
            {
                return TryBuildAwaiterSource(instruction, method, arity: 2) ?? instruction;
            }
        }

        MethodReference? mapped = Mapper.MapMethod(method);
        if (mapped is not null)
        {
            instruction.Operand = mapped;
            IsMethodBodyModified = true;
            RecordMethod(method);
        }

        return instruction;
    }

    private Instruction? TryBuildAwaiterSource(Instruction instruction, MethodReference method, int arity)
    {
        MethodReference? constructor = Mapper.BuildAwaiterSourceConstructor(method, arity);
        if (constructor is null)
        {
            return null;
        }

        Instruction replacement = Processor!.Create(OpCodes.Newobj, constructor);
        Replace(instruction, replacement);
        RecordType(constructor.DeclaringType, method.ReturnType);
        return replacement;
    }

    private MemberSubstitutionMapper BuildMapper() =>
        MemberSubstitutionMapper.Build(Session, (rule, error) =>
        {
            Session.AddDiagnostic(RewriteDiagnostic.Warning(
                RewriteDiagnosticIds.UnresolvedReplacement,
                $"{error} Member-aware substitution rule '{rule.Id}' was skipped."));
            Session.AddUnresolvedReference(rule.Replacement.ToCanonicalString());
        });

    private void RecordMethod(MethodReference original)
    {
        string normalized = CecilNames.NormalizedTypeFullName(
            original is GenericInstanceMethod generic ? generic.ElementMethod.DeclaringType : original.DeclaringType);
        Record(normalized, original.DeclaringType.FullName);
    }

    private void RecordType(TypeReference mapped, TypeReference original)
    {
        string normalized = CecilNames.NormalizedTypeFullName(original);
        Record(normalized, mapped.FullName);
    }

    private void Record(string normalizedOriginal, string replacement)
    {
        string? ruleId = Mapper.RuleIdFor(normalizedOriginal);
        if (ruleId is null || Method is null)
        {
            return;
        }

        Instruction? first = Method.HasBody ? Method.Body.Instructions.FirstOrDefault() : null;
        string? file = null;
        int line = -1;
        if (first is not null)
        {
            RewriteSession.TryGetSequencePoint(Method, first, out file, out line);
        }

        Session.AddTransformation(new ManifestTransformation(
            ruleId,
            RewriteOperationKind.SubstituteType,
            TransformationOutcome.Transformed,
            SimulationApiPolicy.Controlled,
            normalizedOriginal,
            replacement,
            CecilNames.FullyQualifiedMethodName(Method),
            0,
            file,
            line));
    }
}
