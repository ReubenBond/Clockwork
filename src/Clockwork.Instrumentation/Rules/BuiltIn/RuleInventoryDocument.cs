using System.Collections.Immutable;
using System.Text;
using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// Renders the deterministic BCL rule inventory (<see cref="BuiltInRuleSets.DeterministicBclInventory"/>)
/// as stable Markdown. The output is committed to <c>docs/rule-inventory.md</c> and verified against
/// this renderer by a test, so the published inventory can never silently drift from the shipped rule
/// set. The rendering is deterministic: families and rules follow their canonical declared order.
/// </summary>
public static class RuleInventoryDocument
{
    /// <summary>Renders the full inventory document.</summary>
    /// <returns>The Markdown text, using <c>\n</c> line endings and a trailing newline.</returns>
    public static string Render()
    {
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');

        Line("# Deterministic BCL rule inventory");
        Line();
        Line("<!-- Generated from Clockwork.Instrumentation.Rules.BuiltIn.RuleInventoryDocument.Render().");
        Line("     Do not edit by hand; a test verifies this file matches the shipped rule set. -->");
        Line();
        Line($"Rule set id: `{BuiltInRuleSets.DeterministicBclId}`  ");
        Line($"Version: `{BuiltInRuleSets.DeterministicBclVersion}`  ");
        Line($"Shim assembly: `{BuiltInRuleSets.ShimAssemblyName}`");
        Line();
        Line(
            "This is the exact, exhaustive surface the built-in rule set redirects. Every other API is " +
            "**not** rewritten. Outside an active simulation each shim runs the real BCL API unchanged; " +
            "under an active simulation with no registered runtime environment the shim fails explicitly " +
            "rather than fall back to real time or randomness.");
        Line();

        foreach (BuiltInRuleFamily family in BuiltInRuleSets.AllFamilies)
        {
            ImmutableArray<RewriteRule> rules =
                [.. BuiltInRuleSets.DeterministicBclInventory.Where(e => e.Family == family).Select(e => e.Rule)];

            Line($"## {family} family");
            Line();
            Line($"Policy: **{DescribePolicy(rules)}**. {DescribeFamily(family)}");
            Line();
            Line("| Rule id | BCL target | Shim | Policy |");
            Line("| --- | --- | --- | --- |");
            foreach (RewriteRule rule in rules)
            {
                string target = rule.Operation == RewriteOperationKind.RedirectNewObj
                    ? "new " + rule.Target.DeclaringTypeFullName + ParamSuffix(rule.Target.ParameterTypeFullNames)
                    : rule.Target.ToCanonicalString();
                Line($"| `{rule.Id}` | `{target}` | `{rule.Replacement.ToCanonicalString()}` | {rule.Policy} |");
            }

            Line();
        }

        Line("## Documented holes (not rewritten in this rule set)");
        Line();
        Line("These nondeterministic or entropy-drawing surfaces are intentionally **not** covered by");
        Line("Phase 5 and remain real BCL calls even under simulation:");
        Line();
        Line("- `Stopwatch` instance APIs (`Start`/`Stop`/`Restart`/`Elapsed`/`ElapsedMilliseconds`/`ElapsedTicks`) and the `GetElapsedTime(long, long)` overload.");
        Line("- Generic cryptographic helpers `RandomNumberGenerator.GetItems<T>` and `Shuffle<T>`, and any `GetString`/`GetHexString` overloads beyond those listed above.");
        Line("- `DateTime`/`DateTimeOffset` parsing/formatting and any culture-, timezone-, or kind-conversion helpers other than the `Now`/`UtcNow`/`Today` clocks above.");
        Line("- Everything outside time/identity/random: task/thread/synchronization primitives, timers, collections, Buggify, hosting, and network/HTTP. These are out of scope for Phase 5.");
        Line();
        Line("Determinism is claimed **only** for the exact rules tabulated above.");

        return sb.ToString();
    }

    private static string DescribePolicy(ImmutableArray<RewriteRule> rules) =>
        rules.All(r => r.Policy == SimulationApiPolicy.Rejected) ? "Rejected" : "Controlled";

    private static string DescribeFamily(BuiltInRuleFamily family) => family switch
    {
        BuiltInRuleFamily.Clock =>
            "Wall-clock, offset-clock, monotonic timestamp, and tick-counter reads dispatch to the node's " +
            "simulated clock. Local-time APIs honour the configured simulation time zone; tick counters wrap " +
            "with correct `int`/`long` semantics.",
        BuiltInRuleFamily.Identity =>
            "GUIDs draw deterministic bytes while preserving RFC 4122 variant and version. `CreateVersion7` " +
            "encodes the simulated UTC millisecond timestamp in the first 48 bits; repeated calls at the same " +
            "simulated instant share that timestamp (no monotonicity guarantee beyond the BCL contract).",
        BuiltInRuleFamily.Random =>
            "`Random.Shared` and unseeded `new Random()` become per-node deterministic streams isolated from " +
            "the scheduler/network/Buggify seed domains; explicitly seeded `new Random(int)` preserves the " +
            "caller's seed exactly, matching normal BCL behaviour.",
        BuiltInRuleFamily.Crypto =>
            "Static entropy APIs are redirected to a policy shim. The default under simulation is a precise " +
            "rejected-call diagnostic; a test-only opt-in can substitute deterministic-insecure bytes. " +
            "Production security semantics are never changed.",
        _ => string.Empty,
    };

    private static string ParamSuffix(ImmutableArray<string> parameters) =>
        parameters.IsDefault ? "(*)" : "(" + string.Join(",", parameters) + ")";
}
