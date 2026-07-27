using System.Collections.Immutable;
using Clockwork.Instrumentation.Configuration;

namespace Clockwork.Tool;

/// <summary>
/// Builds an <see cref="InstrumentationConfiguration"/> from command-line options, optionally layered
/// over a JSON configuration file. When a configuration file is supplied it is the source of the
/// policy settings; rule-set paths given on the command line are always appended so a caller can add
/// documents without editing the file. This mirrors the MSBuild task's precedence so the CLI and the
/// build produce identical configurations from equivalent inputs.
/// </summary>
internal static class ConfigurationFactory
{
    /// <summary>The value options this factory understands, contributed to a command's option set.</summary>
    public static readonly ImmutableArray<string> ValueOptions =
    [
        "config", "rule-set", "include", "exclude", "r2r", "strong-name",
        "key", "target-runtime", "exclude-framework", "rewrite-dependencies",
    ];

    /// <summary>Builds the configuration described by the reader's options.</summary>
    /// <param name="reader">The parsed command arguments.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="ConfigurationException">A configuration file or option value was invalid.</exception>
    /// <exception cref="UsageException">An enum option value was not recognized.</exception>
    public static InstrumentationConfiguration Build(ArgumentReader reader)
    {
        string? configPath = reader.GetString("config");
        InstrumentationConfiguration configuration = configPath is not null
            ? InstrumentationConfigurationLoader.Load(configPath)
            : new InstrumentationConfiguration
            {
                IncludePatterns = [.. reader.GetMany("include")],
                ExcludePatterns = [.. reader.GetMany("exclude")],
                ExcludeFrameworkAssemblies = reader.GetBool("exclude-framework", true),
                RewriteDependencies = reader.GetBool("rewrite-dependencies", true),
                TargetRuntime = ParseVersion(reader.GetString("target-runtime")),
                ReadyToRunPolicy = ParseEnum<ReadyToRunPolicy>(reader.GetString("r2r"), ReadyToRunPolicy.Reject),
                StrongNamePolicy = ParseEnum<StrongNamePolicy>(reader.GetString("strong-name"), StrongNamePolicy.Fail),
                StrongNameKeyPath = reader.GetString("key"),
            };

        IReadOnlyList<string> extraRuleSets = reader.GetMany("rule-set");
        if (extraRuleSets.Count > 0)
        {
            configuration = configuration with
            {
                RuleSetPaths = [.. configuration.RuleSetPaths, .. extraRuleSets.Select(Path.GetFullPath)],
            };
        }

        return configuration;
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value, out Version? version)
            ? version
            : throw new UsageException($"'--target-runtime' value '{value}' is not a valid version.");
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse(value, ignoreCase: false, out TEnum result) && Enum.IsDefined(result))
        {
            return result;
        }

        string allowed = string.Join(", ", Enum.GetNames<TEnum>());
        throw new UsageException($"'{value}' is not one of: {allowed}.");
    }
}
