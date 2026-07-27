using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Clockwork.Instrumentation.Build;

/// <summary>
/// The MSBuild task that runs Clockwork's deterministic IL instrumentation over a resolved
/// application output directory and stages the instrumented closure under <c>obj</c>. It is opt-in:
/// the accompanying targets only invoke it when <c>ClockworkInstrumentationEnabled</c> is
/// <see langword="true"/>. The task builds an <see cref="InstrumentationConfiguration"/> from either a
/// configuration file or its own properties, merges the configured rule-set documents, runs the
/// <see cref="InstrumentationRunner"/>, and surfaces every rewrite diagnostic as an MSBuild
/// message/warning/error so a targeted failure fails the build rather than silently degrading output.
/// </summary>
public sealed class ClockworkInstrumentTask : MSBuildTask
{
    /// <summary>Gets or sets the resolved application output directory to instrument.</summary>
    [Required]
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the staging directory the instrumented closure is written to (owned by the task).</summary>
    [Required]
    public string StagingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional JSON configuration file path. When set, it is the source of policy settings.</summary>
    public string? ConfigurationPath { get; set; }

    /// <summary>Gets or sets the rule-set JSON documents to load and merge (appended to any from the configuration file).</summary>
    public ITaskItem[] RuleSetPaths { get; set; } = [];

    /// <summary>Gets or sets the built-in rule set ids to enable (e.g. <c>clockwork.bcl.deterministic</c>). Merged at lowest precedence.</summary>
    public ITaskItem[] BuiltInRuleSets { get; set; } = [];

    /// <summary>Gets or sets the built-in rule families to include (empty includes every family).</summary>
    public ITaskItem[] BuiltInIncludeFamilies { get; set; } = [];

    /// <summary>Gets or sets the built-in rule families to exclude even when included.</summary>
    public ITaskItem[] BuiltInExcludeFamilies { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether built-in selection is strict (the crypto guard cannot be excluded). Defaults to <see langword="true"/>.</summary>
    public bool StrictBuiltIns { get; set; } = true;

    /// <summary>Gets or sets the include glob patterns selecting assemblies eligible for rewriting.</summary>
    public ITaskItem[] IncludePatterns { get; set; } = [];

    /// <summary>Gets or sets the exclude glob patterns removing assemblies from rewriting.</summary>
    public ITaskItem[] ExcludePatterns { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether framework/reference assemblies are excluded. Defaults to <see langword="true"/>.</summary>
    public bool ExcludeFrameworkAssemblies { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether managed dependencies are rewritten too. Defaults to <see langword="true"/>.</summary>
    public bool RewriteDependencies { get; set; } = true;

    /// <summary>Gets or sets the ReadyToRun policy name (<c>Reject</c> or <c>StripToIL</c>).</summary>
    public string ReadyToRunPolicy { get; set; } = nameof(Configuration.ReadyToRunPolicy.Reject);

    /// <summary>Gets or sets the strong-name policy name (<c>Fail</c> or <c>ReSign</c>).</summary>
    public string StrongNamePolicy { get; set; } = nameof(Configuration.StrongNamePolicy.Fail);

    /// <summary>Gets or sets the strong-name key path used when the strong-name policy is <c>ReSign</c>.</summary>
    public string? StrongNameKeyPath { get; set; }

    /// <summary>Gets or sets the target runtime version rules are evaluated against, or empty to disable filtering.</summary>
    public string? TargetRuntime { get; set; }

    /// <summary>Gets or sets the entry assembly's simple name or file name, or empty to auto-detect it.</summary>
    public string? EntryAssemblyName { get; set; }

    /// <summary>Gets or sets the path the closure manifest is written to, or empty for the default sibling path.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Gets or sets the path to the atomic success marker used for incremental builds.</summary>
    public string? CachePath { get; set; }

    /// <summary>Gets the path the closure manifest was written to.</summary>
    [Output]
    public string ResolvedManifestPath { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the run was satisfied incrementally from cache.</summary>
    [Output]
    public bool WasIncremental { get; private set; }

    /// <inheritdoc/>
    public override bool Execute()
    {
        InstrumentationConfiguration configuration;
        RewriteRuleSet ruleSet;
        try
        {
            configuration = BuildConfiguration();
            ruleSet = RuleSetMerge.LoadAndMerge(configuration).RuleSet;
        }
        catch (Exception ex) when (ex is ConfigurationException or RuleSetFormatException)
        {
            Log.LogError(null, "CWR0200", null, ConfigurationPath, 0, 0, 0, 0, ex.Message);
            return false;
        }

        InstrumentationResult result;
        try
        {
            var request = new InstrumentationRequest
            {
                SourceDirectory = SourceDirectory,
                StagingDirectory = StagingDirectory,
                Configuration = configuration,
                RuleSet = ruleSet,
                EntryAssemblyName = NullIfEmpty(EntryAssemblyName),
            };

            if (NullIfEmpty(ManifestPath) is { } manifestOverride)
            {
                request = request with { ManifestPath = manifestOverride };
            }

            if (NullIfEmpty(CachePath) is { } cacheOverride)
            {
                request = request with { CachePath = cacheOverride };
            }

            result = InstrumentationRunner.Run(request);
        }
        catch (ClosureException ex)
        {
            Log.LogError(null, "CWR0201", null, SourceDirectory, 0, 0, 0, 0, ex.Message);
            return false;
        }

        ResolvedManifestPath = result.ManifestPath;
        WasIncremental = result.WasIncrementalHit;

        foreach (AssemblyInstrumentationResult assembly in result.Assemblies)
        {
            foreach (RewriteDiagnostic diagnostic in assembly.Diagnostics)
            {
                Report(diagnostic);
            }
        }

        foreach (RewriteDiagnostic diagnostic in result.Diagnostics)
        {
            Report(diagnostic);
        }

        if (result.WasIncrementalHit)
        {
            Log.LogMessage(MessageImportance.Low, "Clockwork instrumentation is up to date (incremental).");
        }
        else if (result.Succeeded)
        {
            Log.LogMessage(
                MessageImportance.Normal,
                "Clockwork instrumented {0} assemblies ({1} verified no-ops), copied {2} assets to '{3}'.",
                result.RewrittenCount,
                result.NoOpCount,
                result.CopiedAssets.Length,
                result.StagingDirectory);
        }

        return result.Succeeded && !Log.HasLoggedErrors;
    }

    private InstrumentationConfiguration BuildConfiguration()
    {
        InstrumentationConfiguration configuration = NullIfEmpty(ConfigurationPath) is { } path
            ? InstrumentationConfigurationLoader.Load(path)
            : new InstrumentationConfiguration
            {
                IncludePatterns = ToPatternArray(IncludePatterns),
                ExcludePatterns = ToPatternArray(ExcludePatterns),
                ExcludeFrameworkAssemblies = ExcludeFrameworkAssemblies,
                RewriteDependencies = RewriteDependencies,
                TargetRuntime = ParseVersion(TargetRuntime),
                ReadyToRunPolicy = ParseEnum<ReadyToRunPolicy>(ReadyToRunPolicy, nameof(ReadyToRunPolicy)),
                StrongNamePolicy = ParseEnum<StrongNamePolicy>(StrongNamePolicy, nameof(StrongNamePolicy)),
                StrongNameKeyPath = NullIfEmpty(StrongNameKeyPath),
            };

        System.Collections.Immutable.ImmutableArray<string> taskRuleSets = ToPatternArray(RuleSetPaths);
        if (!taskRuleSets.IsDefaultOrEmpty)
        {
            configuration = configuration with
            {
                RuleSetPaths = [.. configuration.RuleSetPaths, .. taskRuleSets],
            };
        }

        System.Collections.Immutable.ImmutableArray<string> builtInIds = ToPatternArray(BuiltInRuleSets);
        if (!builtInIds.IsDefaultOrEmpty)
        {
            configuration = configuration with
            {
                BuiltInRuleSetIds = [.. configuration.BuiltInRuleSetIds.Concat(builtInIds).Distinct(StringComparer.Ordinal)],
            };
        }

        System.Collections.Immutable.ImmutableArray<string> includeFamilies = ToPatternArray(BuiltInIncludeFamilies);
        if (!includeFamilies.IsDefaultOrEmpty)
        {
            configuration = configuration with { BuiltInIncludeFamilies = includeFamilies };
        }

        System.Collections.Immutable.ImmutableArray<string> excludeFamilies = ToPatternArray(BuiltInExcludeFamilies);
        if (!excludeFamilies.IsDefaultOrEmpty)
        {
            configuration = configuration with { BuiltInExcludeFamilies = excludeFamilies };
        }

        // Only override strictness when the configuration did not come from a file, or when explicitly
        // relaxed, so a config file's stricter setting is never silently loosened by the default.
        if (NullIfEmpty(ConfigurationPath) is null || !StrictBuiltIns)
        {
            configuration = configuration with { StrictBuiltIns = StrictBuiltIns };
        }

        return configuration;
    }

    private void Report(RewriteDiagnostic diagnostic)
    {
        switch (diagnostic.Severity)
        {
            case RewriteDiagnosticSeverity.Error:
                Log.LogError(null, diagnostic.Id, null, null, 0, 0, 0, 0, diagnostic.Message);
                break;
            case RewriteDiagnosticSeverity.Warning:
                Log.LogWarning(null, diagnostic.Id, null, null, 0, 0, 0, 0, diagnostic.Message);
                break;
            default:
                Log.LogMessage(MessageImportance.Low, "{0}: {1}", diagnostic.Id, diagnostic.Message);
                break;
        }
    }

    private static System.Collections.Immutable.ImmutableArray<string> ToPatternArray(ITaskItem[] items) =>
        items.Length == 0
            ? []
            : [.. items.Select(i => i.ItemSpec).Where(s => !string.IsNullOrWhiteSpace(s))];

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Version? ParseVersion(string? value)
    {
        if (NullIfEmpty(value) is not { } text)
        {
            return null;
        }

        return Version.TryParse(text, out Version? version)
            ? version
            : throw new ConfigurationException($"TargetRuntime '{text}' is not a valid version.");
    }

    private static TEnum ParseEnum<TEnum>(string value, string propertyName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: false, out TEnum result) && Enum.IsDefined(result))
        {
            return result;
        }

        string allowed = string.Join(", ", Enum.GetNames<TEnum>());
        throw new ConfigurationException($"{propertyName} value '{value}' is not one of: {allowed}.");
    }
}
