using System.Collections.Immutable;
using Clockwork.Instrumentation.Diagnostics;

namespace Clockwork.Instrumentation.Rules;

/// <summary>
/// How a rule set's rules support one target runtime. A rule set supports a target when every rule supports it or has
/// an alternative: a rule with the same target that supports it, as when a rule set carries one rule per runtime for an
/// API. Rewriting requires that support, so no call site can match only a rule classified for another runtime.
/// </summary>
/// <param name="TargetRuntime">The target runtime's feature version.</param>
/// <param name="SupportedRuntimes">
/// The runtimes every rule that supports <paramref name="TargetRuntime"/> also supports: the range recorded on each
/// rewritten assembly and checked when a simulation starts.
/// </param>
/// <param name="UnsupportedRules">The rules that neither support the target nor have an alternative that does.</param>
public sealed record RuleSetRuntimeSupport(
    Version TargetRuntime,
    RuntimeVersionRange SupportedRuntimes,
    ImmutableArray<RewriteRule> UnsupportedRules)
{
    private const int ExampleRuleCount = 3;

    /// <summary>Gets a value indicating whether the rule set supports <see cref="TargetRuntime"/>.</summary>
    public bool IsSupported => UnsupportedRules.IsEmpty;

    /// <summary>Evaluates how <paramref name="ruleSet"/> supports <paramref name="targetRuntime"/>.</summary>
    /// <param name="ruleSet">The rule set.</param>
    /// <param name="targetRuntime">The target runtime's feature version.</param>
    /// <returns>The rule set's support for the target.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="targetRuntime"/> or a rule's supported-runtime bound is not a feature version.
    /// </exception>
    public static RuleSetRuntimeSupport Evaluate(RewriteRuleSet ruleSet, Version targetRuntime)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(targetRuntime);
        if (!RuntimeFeatureVersion.IsFeatureVersion(targetRuntime))
        {
            throw new ArgumentException(
                $"Target runtime '{targetRuntime}' is not a feature version (major.minor).", nameof(targetRuntime));
        }

        var supportedTargets = new HashSet<string>(StringComparer.Ordinal);
        RuntimeVersionRange supportedRuntimes = RuntimeVersionRange.All;
        foreach (RewriteRule rule in ruleSet.Rules)
        {
            if (!rule.SupportedRuntimes.HasFeatureVersionBounds)
            {
                throw new ArgumentException(
                    $"Rule '{rule.Id}' supports runtimes {rule.SupportedRuntimes.ToDisplayString()}, whose bounds are not " +
                    "feature versions (major.minor).",
                    nameof(ruleSet));
            }

            if (rule.SupportedRuntimes.Includes(targetRuntime))
            {
                supportedTargets.Add(rule.Target.ToCanonicalString());
                supportedRuntimes = supportedRuntimes.Intersect(rule.SupportedRuntimes);
            }
        }

        ImmutableArray<RewriteRule> unsupported =
        [
            .. ruleSet.Rules.Where(rule =>
                !rule.SupportedRuntimes.Includes(targetRuntime)
                && !supportedTargets.Contains(rule.Target.ToCanonicalString())),
        ];
        return new RuleSetRuntimeSupport(targetRuntime, supportedRuntimes, unsupported);
    }

    /// <summary>Creates the error reporting that the rule set does not support the target runtime.</summary>
    /// <param name="ruleSet">The evaluated rule set.</param>
    /// <param name="targetRuntimeOrigin">Where the target runtime came from, such as <c>configured</c>.</param>
    /// <returns>The <see cref="RewriteDiagnosticIds.RuntimeOutOfRange"/> error.</returns>
    public RewriteDiagnostic ToDiagnostic(RewriteRuleSet ruleSet, string targetRuntimeOrigin)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentException.ThrowIfNullOrEmpty(targetRuntimeOrigin);
        if (IsSupported)
        {
            throw new InvalidOperationException($"Rule set '{ruleSet.Id}' supports target runtime {TargetRuntime}.");
        }

        string ranges = string.Join(
            ", ",
            UnsupportedRules
                .GroupBy(rule => rule.SupportedRuntimes)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key.ToDisplayString(), StringComparer.Ordinal)
                .Select(group => $"{group.Key.ToDisplayString()} ({group.Count()} rules)"));
        string examples = string.Join(", ", UnsupportedRules.Take(ExampleRuleCount).Select(rule => $"'{rule.Id}'"));
        return RewriteDiagnostic.Error(
            RewriteDiagnosticIds.RuntimeOutOfRange,
            $"Target runtime {TargetRuntime} ({targetRuntimeOrigin}) is outside the runtimes {UnsupportedRules.Length} of " +
            $"the {ruleSet.Rules.Length} rules in rule set '{ruleSet.Id}' v{ruleSet.Version} support: {ranges}; for " +
            $"example {examples}. Instrument for a runtime the rules support, or select rules classified for " +
            $"{TargetRuntime}. Nothing was rewritten.");
    }
}
