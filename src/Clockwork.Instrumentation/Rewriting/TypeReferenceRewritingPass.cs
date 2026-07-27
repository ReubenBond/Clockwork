using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rules;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Rewrites type-reference operands in method bodies (<c>newarr</c>, <c>castclass</c>, <c>isinst</c>,
/// <c>box</c>, <c>unbox</c>/<c>unbox.any</c>, <c>ldtoken</c>, <c>initobj</c>, <c>sizeof</c>, and the
/// <c>constrained.</c> prefix) whose type matches a <see cref="RewriteOperationKind.SubstituteType"/>
/// rule, replacing them with a reference to the substitute type imported into the target module. The
/// pass is a no-op when the rule set declares no type substitutions.
/// </summary>
internal sealed class TypeReferenceRewritingPass : RewritePass
{
    public TypeReferenceRewritingPass(RewriteSession session)
        : base(session)
    {
    }

    /// <inheritdoc/>
    protected override Instruction VisitInstruction(Instruction instruction)
    {
        if (!Session.Matcher.HasTypeSubstitutions || instruction.Operand is not TypeReference type)
        {
            return instruction;
        }

        if (!Session.Matcher.TryMatchType(type, out RewriteRule rule))
        {
            return instruction;
        }

        if (!Session.Resolver.TryResolveType(Session.TargetModule, rule.Replacement, out TypeReference imported, out string? error))
        {
            string containing = CecilNames.FullyQualifiedMethodName(Method!);
            if (rule.Fallback == RewriteFallback.Skip)
            {
                Session.AddDiagnostic(Diagnostics.RewriteDiagnostic.Warning(
                    Diagnostics.RewriteDiagnosticIds.UnresolvedReplacement,
                    $"{error} Rule '{rule.Id}' was skipped per its fallback policy.",
                    containing,
                    instruction.Offset));
            }
            else
            {
                Session.AddDiagnostic(Diagnostics.RewriteDiagnostic.Error(
                    Diagnostics.RewriteDiagnosticIds.UnresolvedReplacement,
                    $"{error} Rule '{rule.Id}' could not be applied.",
                    containing,
                    instruction.Offset));
            }

            Session.AddUnresolvedReference(rule.Replacement.ToCanonicalString());
            return instruction;
        }

        int offset = instruction.Offset;
        RewriteSession.TryGetSequencePoint(Method!, instruction, out string? file, out int line);
        instruction.Operand = imported;
        IsMethodBodyModified = true;

        Session.AddTransformation(new ManifestTransformation(
            rule.Id,
            rule.Operation,
            TransformationOutcome.Transformed,
            rule.Policy,
            rule.Target.ToCanonicalString(),
            rule.Replacement.ToCanonicalString(),
            CecilNames.FullyQualifiedMethodName(Method!),
            offset,
            file,
            line));

        return instruction;
    }
}
