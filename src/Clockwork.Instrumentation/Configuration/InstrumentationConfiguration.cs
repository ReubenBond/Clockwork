using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// The declarative, serializable configuration that drives the build task and CLI: which rule-set
/// documents to load, which assemblies in a closure to include or exclude, whether to recurse into
/// managed dependencies, and the strong-name / ReadyToRun policies to enforce. Like
/// <see cref="Rules.RewriteRuleSet"/> it is pure data - loading it never executes arbitrary code -
/// and it exposes a stable <see cref="ComputeSignature"/> so it can participate in incremental
/// build keys and idempotence markers.
/// </summary>
public sealed record InstrumentationConfiguration
{
    /// <summary>The configuration schema version this type understands.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the paths of the rule-set JSON documents to load and merge, in precedence order (a later
    /// document's rule overrides an earlier document's rule with the same id). Resolved relative to
    /// the configuration file when loaded from disk.
    /// </summary>
    public ImmutableArray<string> RuleSetPaths { get; init; } = [];

    /// <summary>
    /// Gets the instrumentation granularity. The default <see cref="InstrumentationMode.Controlled"/>
    /// performs only configured controlled-API rewriting; <see cref="InstrumentationMode.RaceExploration"/>
    /// additionally injects fine-grained memory and control-flow scheduling points.
    /// </summary>
    public InstrumentationMode Mode { get; init; } = InstrumentationMode.Controlled;

    /// <summary>
    /// Gets the ids of the built-in rule sets to enable (see
    /// <see cref="Rules.BuiltIn.BuiltInRuleSets.AvailableIds"/>). Built-in rule sets are merged at the
    /// <em>lowest</em> precedence, so a document in <see cref="RuleSetPaths"/> can override any
    /// built-in rule by id. Empty (the default) enables no built-ins, preserving prior behaviour.
    /// </summary>
    public ImmutableArray<string> BuiltInRuleSetIds { get; init; } = [];

    /// <summary>
    /// Gets the built-in rule families to include (see <see cref="Rules.BuiltIn.BuiltInRuleFamily"/>).
    /// Empty (the default) includes every family. Applied to every enabled built-in rule set.
    /// </summary>
    public ImmutableArray<string> BuiltInIncludeFamilies { get; init; } = [];

    /// <summary>
    /// Gets the built-in rule families to exclude even when included. Exclusion always wins. Excluding
    /// the <c>Crypto</c> family is only permitted when <see cref="StrictBuiltIns"/> is
    /// <see langword="false"/>, so a strict build cannot silently drop the crypto-randomness guard.
    /// </summary>
    public ImmutableArray<string> BuiltInExcludeFamilies { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether built-in rule selection is strict. Defaults to
    /// <see langword="true"/>: the crypto-randomness guard family cannot be excluded, so a build cannot
    /// accidentally ship without rejecting non-deterministic cryptographic randomness.
    /// </summary>
    public bool StrictBuiltIns { get; init; } = true;

    /// <summary>
    /// Gets the case-insensitive file-name glob patterns (<c>*</c>/<c>?</c>) selecting which
    /// assemblies in the discovered closure are eligible for rewriting. An empty list includes every
    /// non-excluded managed assembly.
    /// </summary>
    public ImmutableArray<string> IncludePatterns { get; init; } = [];

    /// <summary>
    /// Gets the case-insensitive file-name glob patterns selecting assemblies to exclude from
    /// rewriting even if they match an include pattern. Exclusion always wins over inclusion.
    /// </summary>
    public ImmutableArray<string> ExcludePatterns { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether framework and reference assemblies (the shared runtime, and
    /// anything under a reference-assemblies directory) are excluded from rewriting. Defaults to
    /// <see langword="true"/>; the deterministic kernel never rewrites the BCL.
    /// </summary>
    public bool ExcludeFrameworkAssemblies { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether managed dependencies reachable from the primary assemblies are
    /// rewritten too (the application/dependency closure), subject to include/exclude filtering.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool RewriteDependencies { get; init; } = true;

    /// <summary>
    /// Gets the target runtime version rules are evaluated against (see
    /// <see cref="Rewriting.RewriteOptions.TargetRuntime"/>). <see langword="null"/> disables runtime
    /// filtering.
    /// </summary>
    public Version? TargetRuntime { get; init; }

    /// <summary>Gets the policy for handling ReadyToRun inputs. Defaults to <see cref="ReadyToRunPolicy.Reject"/>.</summary>
    public ReadyToRunPolicy ReadyToRunPolicy { get; init; } = ReadyToRunPolicy.Reject;

    /// <summary>Gets the policy for handling strong-named inputs. Defaults to <see cref="StrongNamePolicy.Fail"/>.</summary>
    public StrongNamePolicy StrongNamePolicy { get; init; } = StrongNamePolicy.Fail;

    /// <summary>
    /// Gets the path of the strong-name key (<c>.snk</c>) used when <see cref="StrongNamePolicy"/> is
    /// <see cref="StrongNamePolicy.ReSign"/>. Resolved relative to the configuration file when loaded
    /// from disk.
    /// </summary>
    public string? StrongNameKeyPath { get; init; }

    /// <summary>
    /// Computes a stable SHA-256 signature over every field that affects the rewritten output. Used
    /// as part of the incremental build key so a configuration change invalidates staged outputs.
    /// </summary>
    /// <returns>The lower-case hex signature.</returns>
    public string ComputeSignature() =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalString())));

    /// <summary>Returns a stable canonical multi-line description of the configuration.</summary>
    /// <returns>The canonical string.</returns>
    public string ToCanonicalString()
    {
        var builder = new StringBuilder();
        builder.Append("schema:").Append(CurrentSchemaVersion).Append('\n');
        builder.Append("mode:").Append(Mode).Append('\n');
        AppendList(builder, "ruleSets", RuleSetPaths);
        AppendList(builder, "builtInRuleSets", BuiltInRuleSetIds);
        AppendList(builder, "builtInInclude", BuiltInIncludeFamilies);
        AppendList(builder, "builtInExclude", BuiltInExcludeFamilies);
        builder.Append("strictBuiltIns:").Append(StrictBuiltIns).Append('\n');
        AppendList(builder, "include", IncludePatterns);
        AppendList(builder, "exclude", ExcludePatterns);
        builder.Append("excludeFramework:").Append(ExcludeFrameworkAssemblies).Append('\n');
        builder.Append("rewriteDependencies:").Append(RewriteDependencies).Append('\n');
        builder.Append("targetRuntime:").Append(TargetRuntime?.ToString() ?? "*").Append('\n');
        builder.Append("r2rPolicy:").Append(ReadyToRunPolicy).Append('\n');
        builder.Append("strongNamePolicy:").Append(StrongNamePolicy).Append('\n');
        builder.Append("strongNameKey:").Append(StrongNameKeyPath ?? string.Empty).Append('\n');
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string label, ImmutableArray<string> values)
    {
        builder.Append(label).Append(':');
        builder.Append(string.Join(",", values.Select(v => v.Replace(",", "%2C", StringComparison.Ordinal))));
        builder.Append('\n');
    }
}
