using Clockwork.Instrumentation.Rules;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Rewriting;

/// <summary>
/// Indexes a <see cref="RewriteRuleSet"/> for fast, deterministic matching of Mono.Cecil references
/// during a single rewrite, honouring the configured target-runtime version. Member-operation rules
/// (call/newobj/wrap/reject) are keyed by declaring-type full name; type-substitution rules are
/// keyed by target-type full name.
/// </summary>
internal sealed class RewriteRuleMatcher
{
    private readonly Dictionary<string, List<RewriteRule>> _memberRules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RewriteRule> _typeSubstitutions = new(StringComparer.Ordinal);
    private readonly Version? _targetRuntime;

    public RewriteRuleMatcher(RewriteRuleSet ruleSet, Version? targetRuntime)
    {
        _targetRuntime = targetRuntime;
        foreach (RewriteRule rule in ruleSet.Rules)
        {
            if (rule.Operation == RewriteOperationKind.SubstituteType)
            {
                // First rule for a given type wins (rule sets forbid duplicate ids, but two rules
                // could target the same type; the earlier one takes precedence deterministically).
                _ = _typeSubstitutions.TryAdd(rule.Target.DeclaringTypeFullName, rule);
            }
            else
            {
                if (!_memberRules.TryGetValue(rule.Target.DeclaringTypeFullName, out List<RewriteRule>? list))
                {
                    list = [];
                    _memberRules[rule.Target.DeclaringTypeFullName] = list;
                }

                list.Add(rule);
            }
        }
    }

    /// <summary>Gets a value indicating whether the rule set contains any type-substitution rules.</summary>
    public bool HasTypeSubstitutions => _typeSubstitutions.Count > 0;

    /// <summary>
    /// Finds the first rule (in declared order) matching an invocation of <paramref name="method"/>
    /// at a call/newobj site. Returns <see langword="false"/> if no rule applies. A rule whose target
    /// runtime is out of range is reported via <paramref name="outOfRangeRule"/> so the caller can
    /// diagnose it rather than silently skip a targeted site.
    /// </summary>
    public bool TryMatchInvocation(MethodReference method, bool isNewObj, out RewriteRule matched, out RewriteRule? outOfRangeRule)
    {
        matched = null!;
        outOfRangeRule = null;

        string declaringType = CecilNames.NormalizedTypeFullName(method.DeclaringType);
        if (!_memberRules.TryGetValue(declaringType, out List<RewriteRule>? rules))
        {
            return false;
        }

        foreach (RewriteRule rule in rules)
        {
            if (!AppliesTo(rule.Operation, isNewObj))
            {
                continue;
            }

            if (rule.Target.MemberName != method.Name)
            {
                continue;
            }

            if (!MatchesParameters(rule.Target, method))
            {
                continue;
            }

            if (!rule.SupportedRuntimes.Includes(_targetRuntime))
            {
                outOfRangeRule ??= rule;
                continue;
            }

            matched = rule;
            return true;
        }

        return false;
    }

    /// <summary>Finds the type-substitution rule for <paramref name="type"/>, if any.</summary>
    public bool TryMatchType(TypeReference type, out RewriteRule matched)
    {
        matched = null!;
        string key = CecilNames.NormalizedTypeFullName(type);
        if (_typeSubstitutions.TryGetValue(key, out RewriteRule? rule) && rule.SupportedRuntimes.Includes(_targetRuntime))
        {
            matched = rule;
            return true;
        }

        return false;
    }

    private static bool AppliesTo(RewriteOperationKind operation, bool isNewObj) => operation switch
    {
        RewriteOperationKind.RedirectNewObj => isNewObj,
        RewriteOperationKind.RedirectCall or RewriteOperationKind.WrapAfterCall => !isNewObj,
        RewriteOperationKind.InjectRejection => true,
        _ => false,
    };

    private static bool MatchesParameters(MemberSignature target, MethodReference method)
    {
        if (!target.HasParameterConstraint)
        {
            return true;
        }

        if (target.ParameterTypeFullNames.Length != method.Parameters.Count)
        {
            return false;
        }

        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (target.ParameterTypeFullNames[i] != CecilNames.ParameterFullName(method.Parameters[i].ParameterType))
            {
                return false;
            }
        }

        return true;
    }
}
