using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rules;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Rewrites <c>call</c>/<c>callvirt</c>/<c>newobj</c> sites that match a member-operation rule:
/// redirecting them to a static replacement, redirecting a constructor to a static factory, inserting
/// a post-call wrapper, or injecting a deterministic rejection before an unsupported invocation.
/// Generic instance methods carry their type arguments onto the replacement when arity matches.
/// </summary>
internal sealed class CallSiteRewritingPass : RewritePass
{
    public CallSiteRewritingPass(RewriteSession session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    protected override Instruction VisitInstruction(Instruction instruction)
    {
        bool isCall = instruction.OpCode.Code is Code.Call or Code.Callvirt;
        bool isNewObj = instruction.OpCode.Code == Code.Newobj;
        if ((!isCall && !isNewObj) || instruction.Operand is not MethodReference method)
        {
            return instruction;
        }

        if (!Session.Matcher.TryMatchInvocation(method, isNewObj, out RewriteRule rule, out RewriteRule? outOfRange))
        {
            if (outOfRange is not null)
            {
                ReportRuntimeOutOfRange(instruction, method, outOfRange);
            }

            return instruction;
        }

        return rule.Operation switch
        {
            RewriteOperationKind.RedirectCall => RedirectCall(instruction, method, rule),
            RewriteOperationKind.RedirectNewObj => RedirectCall(instruction, method, rule),
            RewriteOperationKind.WrapAfterCall => WrapAfterCall(instruction, method, rule),
            RewriteOperationKind.InjectRejection => InjectRejection(instruction, method, rule),
            _ => instruction,
        };
    }

    private Instruction RedirectCall(Instruction instruction, MethodReference method, RewriteRule rule)
    {
        int offset = instruction.Offset;
        if (!TryResolveMethod(rule, method, offset, out MethodReference open, out MethodDefinition definition))
        {
            return instruction;
        }

        MethodReference target = ApplyGenericArguments(open, method, definition);
        var replacement = Instruction.Create(OpCodes.Call, target);
        Record(rule, offset, TransformationOutcome.Transformed);
        Replace(instruction, replacement);
        return replacement;
    }

    private Instruction WrapAfterCall(Instruction instruction, MethodReference method, RewriteRule rule)
    {
        int offset = instruction.Offset;
        if (!TryResolveMethod(rule, method, offset, out MethodReference open, out MethodDefinition definition))
        {
            return instruction;
        }

        MethodReference wrapper = open;
        if (definition.HasGenericParameters && definition.GenericParameters.Count == 1)
        {
            var generic = new GenericInstanceMethod(open);
            generic.GenericArguments.Add(Session.TargetModule.ImportReference(method.ReturnType));
            wrapper = generic;
        }

        var wrap = Instruction.Create(OpCodes.Call, wrapper);
        Record(rule, offset, TransformationOutcome.Transformed);
        Processor!.InsertAfter(instruction, wrap);
        IsMethodBodyModified = true;
        return wrap;
    }

    private Instruction InjectRejection(Instruction instruction, MethodReference method, RewriteRule rule)
    {
        int offset = instruction.Offset;
        if (!TryResolveMethod(rule, method, offset, out MethodReference open, out _))
        {
            return instruction;
        }

        // Insert "ldstr <api>; call Reject(string)" before the invocation, keeping the original
        // invocation in place so the evaluation stack stays balanced and verifiable. Replace() moves
        // branch targets and handler boundaries onto the new leading instruction.
        var apiName = Instruction.Create(OpCodes.Ldstr, CecilNames.FullyQualifiedMethodName(method));
        var reject = Instruction.Create(OpCodes.Call, open);

        Record(rule, offset, TransformationOutcome.Rejected);
        Replace(instruction, apiName);
        Processor!.InsertAfter(apiName, reject);
        Processor.InsertAfter(reject, instruction);
        return instruction;
    }

    private bool TryResolveMethod(RewriteRule rule, MethodReference method, int offset, out MethodReference open, out MethodDefinition definition)
    {
        if (Session.Resolver.TryResolveMethod(Session.TargetModule, rule.Replacement, out open, out definition, out string? error))
        {
            return true;
        }

        string containing = CecilNames.FullyQualifiedMethodName(Method!);
        switch (rule.Fallback)
        {
            case RewriteFallback.Skip:
                Session.AddDiagnostic(RewriteDiagnostic.Warning(
                    RewriteDiagnosticIds.UnresolvedReplacement,
                    $"{error} Rule '{rule.Id}' was skipped per its fallback policy.",
                    containing,
                    offset));
                Record(rule, offset, TransformationOutcome.Skipped);
                break;

            case RewriteFallback.Reject:
                Session.AddDiagnostic(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.UnsupportedTargetShape,
                    $"{error} Rule '{rule.Id}' requires the targeted API not to pass through uncontrolled, but no replacement is available.",
                    containing,
                    offset));
                break;

            default:
                Session.AddDiagnostic(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.UnresolvedReplacement,
                    $"{error} Rule '{rule.Id}' could not be applied.",
                    containing,
                    offset));
                break;
        }

        Session.AddUnresolvedReference(rule.Replacement.ToCanonicalString());
        return false;
    }

    private void ReportRuntimeOutOfRange(Instruction instruction, MethodReference method, RewriteRule rule)
    {
        string containing = CecilNames.FullyQualifiedMethodName(Method!);
        string message =
            $"Rule '{rule.Id}' matched {CecilNames.FullyQualifiedMethodName(method)} but the configured target runtime is outside its supported range {rule.SupportedRuntimes.ToCanonicalString()}.";

        RewriteDiagnostic diagnostic = rule.Fallback == RewriteFallback.Skip
            ? RewriteDiagnostic.Warning(RewriteDiagnosticIds.RuntimeOutOfRange, $"{message} Skipped per fallback policy.", containing, instruction.Offset)
            : RewriteDiagnostic.Error(RewriteDiagnosticIds.RuntimeOutOfRange, message, containing, instruction.Offset);
        Session.AddDiagnostic(diagnostic);
        _ = method;
        Record(rule, instruction.Offset, TransformationOutcome.Skipped);
    }

    private MethodReference ApplyGenericArguments(MethodReference open, MethodReference matched, MethodDefinition definition)
    {
        if (!definition.HasGenericParameters)
        {
            return open;
        }

        // A generic method on a closed generic type - e.g. Task<int>.ContinueWith<string>(...) - binds
        // the replacement's type parameters from the receiver's declaring-type arguments *followed by*
        // the call site's own method type arguments, in that order. The controlled shim therefore
        // declares its generic parameters declaring-type-first (e.g.
        // ControlledTask.ContinueWith<TResult, TNewResult>(Task<TResult>, Func<Task<TResult>, TNewResult>)).
        if (matched is GenericInstanceMethod methodAndType
            && matched.DeclaringType is GenericInstanceType owner
            && definition.GenericParameters.Count == owner.GenericArguments.Count + methodAndType.GenericArguments.Count)
        {
            return Instantiate(open, [.. owner.GenericArguments, .. methodAndType.GenericArguments]);
        }

        // A generic call site - e.g. Task.WhenAll<int>(...) - carries its own method type arguments,
        // which bind the generic replacement method one-for-one.
        if (matched is GenericInstanceMethod generic
            && definition.GenericParameters.Count == generic.GenericArguments.Count)
        {
            return Instantiate(open, generic.GenericArguments);
        }

        // A non-generic member on a closed generic type - e.g. Task<int>.get_Result - carries the
        // receiver's declaring-type arguments onto the generic replacement method, so a redirect to
        // ControlledTask.Result<int>(Task<int>) stays stack-balanced and correctly typed.
        if (matched is not GenericInstanceMethod
            && matched.DeclaringType is GenericInstanceType declaring
            && definition.GenericParameters.Count == declaring.GenericArguments.Count)
        {
            return Instantiate(open, declaring.GenericArguments);
        }

        return open;
    }

    private GenericInstanceMethod Instantiate(MethodReference open, IEnumerable<TypeReference> arguments)
    {
        var result = new GenericInstanceMethod(open);
        foreach (TypeReference argument in arguments)
        {
            result.GenericArguments.Add(Session.TargetModule.ImportReference(argument));
        }

        return result;
    }

    private void Record(RewriteRule rule, int offset, TransformationOutcome outcome)
    {
        string containing = CecilNames.FullyQualifiedMethodName(Method!);
        RewriteSession.TryGetSequencePoint(Method!, FindInstruction(offset), out string? file, out int line);

        Session.AddTransformation(new ManifestTransformation(
            rule.Id,
            rule.Operation,
            outcome,
            rule.Policy,
            rule.Target.ToCanonicalString(),
            outcome == TransformationOutcome.PassedThrough ? null : rule.Replacement.ToCanonicalString(),
            containing,
            offset,
            file,
            line));
    }

    private Instruction FindInstruction(int offset)
    {
        foreach (Instruction instruction in Method!.Body.Instructions)
        {
            if (instruction.Offset == offset)
            {
                return instruction;
            }
        }

        return Method!.Body.Instructions[0];
    }
}
