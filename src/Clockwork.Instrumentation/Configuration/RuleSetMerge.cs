using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;

namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// The outcome of merging several rule-set documents into one effective rule set, including the
/// deterministic record of which rules were overridden by a later document so the merge is auditable
/// rather than silent.
/// </summary>
/// <param name="RuleSet">The merged, effective rule set.</param>
/// <param name="Overrides">The rule ids that a later document redefined, with the winning source.</param>
public readonly record struct RuleSetMergeResult(
    RewriteRuleSet RuleSet,
    ImmutableArray<RuleSetOverride> Overrides);

/// <summary>
/// A single rule id that appeared in more than one document, recording the source that won.
/// </summary>
/// <param name="RuleId">The overridden rule id.</param>
/// <param name="WinningSource">The source name of the document whose definition was used.</param>
public readonly record struct RuleSetOverride(string RuleId, string WinningSource);

/// <summary>
/// Deterministically merges multiple rule-set documents into a single effective
/// <see cref="RewriteRuleSet"/>. Rule sets are supplied in precedence order: when two documents
/// define a rule with the same id, the <em>later</em> document wins, and the override is recorded.
/// The merged set preserves first-seen ordering of surviving rule ids so the result is stable
/// regardless of process or run, and its version is a content hash so any input change alters the
/// idempotence marker.
/// </summary>
public static class RuleSetMerge
{
    /// <summary>The id assigned to a rule set formed by merging more than one document.</summary>
    public const string MergedId = "clockwork.merged";

    /// <summary>Merges the supplied named rule sets in precedence order (later wins on id conflict).</summary>
    /// <param name="sources">The ordered (source name, rule set) pairs.</param>
    /// <returns>The merge result.</returns>
    /// <exception cref="ArgumentException">No sources were supplied.</exception>
    public static RuleSetMergeResult Merge(IReadOnlyList<(string Source, RewriteRuleSet RuleSet)> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one rule set is required to merge.", nameof(sources));
        }

        if (sources.Count == 1)
        {
            return new RuleSetMergeResult(sources[0].RuleSet, []);
        }

        var order = new List<string>();
        var winning = new Dictionary<string, RewriteRule>(StringComparer.Ordinal);
        var winningSource = new Dictionary<string, string>(StringComparer.Ordinal);
        var overrides = new List<RuleSetOverride>();

        foreach ((string source, RewriteRuleSet ruleSet) in sources)
        {
            foreach (RewriteRule rule in ruleSet.Rules)
            {
                if (winning.ContainsKey(rule.Id))
                {
                    overrides.Add(new RuleSetOverride(rule.Id, source));
                }
                else
                {
                    order.Add(rule.Id);
                }

                winning[rule.Id] = rule;
                winningSource[rule.Id] = source;
            }
        }

        var mergedRules = order.Select(id => winning[id]).ToList();

        var canonical = new StringBuilder();
        foreach (RewriteRule rule in mergedRules)
        {
            canonical.Append(rule.ToCanonicalString()).Append('\n');
        }

        string version = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))[..16];

        var merged = new RewriteRuleSet(MergedId, version, mergedRules);
        return new RuleSetMergeResult(
            merged,
            [.. overrides.OrderBy(o => o.RuleId, StringComparer.Ordinal).ThenBy(o => o.WinningSource, StringComparer.Ordinal)]);
    }

    /// <summary>Loads and merges the built-in rule sets and rule-set documents referenced by a configuration.</summary>
    /// <param name="configuration">The configuration whose built-in ids and <see cref="InstrumentationConfiguration.RuleSetPaths"/> are loaded.</param>
    /// <returns>The merge result.</returns>
    /// <exception cref="ConfigurationException">No documents or built-in rule sets are configured, or a built-in selection is invalid.</exception>
    /// <exception cref="RuleSetFormatException">A rule-set document is malformed.</exception>
    public static RuleSetMergeResult LoadAndMerge(InstrumentationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var sources = new List<(string, RewriteRuleSet)>();

        // Built-in rule sets are merged first, so they sit at the lowest precedence: any rule with the
        // same id in a configured document overrides the built-in definition.
        foreach (RewriteRuleSet builtIn in ResolveBuiltIns(configuration))
        {
            sources.Add(($"builtin:{builtIn.Id}", builtIn));
        }

        if (configuration.RuleSetPaths.IsDefaultOrEmpty && sources.Count == 0)
        {
            throw new ConfigurationException(
                "No rule-set documents or built-in rule sets are configured; nothing would be rewritten.");
        }

        if (!configuration.RuleSetPaths.IsDefaultOrEmpty)
        {
            foreach (string path in configuration.RuleSetPaths)
            {
                sources.Add((path, RuleSetJson.Load(path)));
            }
        }

        return Merge(sources);
    }

    /// <summary>
    /// Resolves the enabled built-in rule sets for a configuration, applying family include/exclude
    /// selection and the strict crypto-guard invariant. Returns an empty sequence when no built-in
    /// rule set is enabled.
    /// </summary>
    /// <param name="configuration">The configuration whose built-in selection is resolved.</param>
    /// <returns>The enabled built-in rule sets, in id order.</returns>
    /// <exception cref="ConfigurationException">A built-in id or family name is unknown, or a strict build excludes the crypto guard.</exception>
    public static IReadOnlyList<RewriteRuleSet> ResolveBuiltIns(InstrumentationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.BuiltInRuleSetIds.IsDefaultOrEmpty)
        {
            return [];
        }

        ImmutableArray<BuiltInRuleFamily> families = ResolveFamilies(configuration);

        var result = new List<RewriteRuleSet>();
        foreach (string id in configuration.BuiltInRuleSetIds.Distinct(StringComparer.Ordinal))
        {
            if (!BuiltInRuleSets.IsKnownId(id))
            {
                string known = string.Join(", ", BuiltInRuleSets.AvailableIds);
                throw new ConfigurationException($"Unknown built-in rule set id '{id}'. Known ids: {known}.");
            }

            // Only one built-in rule set exists today; it is family-filtered identically for each id.
            result.Add(BuiltInRuleSets.BuildDeterministicBcl(families));
        }

        return result;
    }

    private static ImmutableArray<BuiltInRuleFamily> ResolveFamilies(InstrumentationConfiguration configuration)
    {
        var included = new HashSet<BuiltInRuleFamily>(
            configuration.BuiltInIncludeFamilies.IsDefaultOrEmpty
                ? BuiltInRuleSets.AllFamilies
                : ParseFamilies(configuration.BuiltInIncludeFamilies, "include"));

        foreach (BuiltInRuleFamily excluded in ParseFamilies(configuration.BuiltInExcludeFamilies, "exclude"))
        {
            if (excluded == BuiltInRuleFamily.Crypto && configuration.StrictBuiltIns)
            {
                throw new ConfigurationException(
                    "The 'Crypto' built-in rule family cannot be excluded while strict built-in selection is enabled. " +
                    "Set strictBuiltIns to false to intentionally drop the cryptographic-randomness guard.");
            }

            included.Remove(excluded);
        }

        return [.. BuiltInRuleSets.AllFamilies.Where(included.Contains)];
    }

    private static IEnumerable<BuiltInRuleFamily> ParseFamilies(ImmutableArray<string> names, string role)
    {
        if (names.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (string name in names)
        {
            if (!BuiltInRuleSets.TryParseFamily(name, out BuiltInRuleFamily family))
            {
                string allowed = string.Join(", ", BuiltInRuleSets.AllFamilies);
                throw new ConfigurationException($"Unknown built-in rule family '{name}' in {role} list. Known families: {allowed}.");
            }

            yield return family;
        }
    }
}
