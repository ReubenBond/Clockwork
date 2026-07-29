using System.Text.Json;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Orchestration;

namespace Clockwork.Instrumentation.Tests.Configuration;

/// <summary>
/// Verifies the instrumentation-configuration loader: valid documents parse with defaults applied,
/// relative paths resolve against the configuration directory, removed fields are rejected, and the
/// signature is stable and change-sensitive.
/// </summary>
public sealed class InstrumentationConfigurationTests
{
    [Fact]
    public void AppliesDefaultsForMinimalDocument()
    {
        InstrumentationConfiguration config =
            InstrumentationConfigurationLoader.Parse("""{ "schemaVersion": 2, "ruleSets": ["r.json"] }""");

        Assert.Equal(InstrumentationMode.Controlled, config.Mode);
        Assert.Null(config.TargetRuntime);
        Assert.Null(config.StrongNameKeyPath);
        Assert.Single(config.RuleSetPaths);
    }

    [Fact]
    public void ParsesAllFields()
    {
        const string doc = """
            {
              "schemaVersion": 2,
              "ruleSets": ["a.json", "b.json"],
              "mode": "RaceExploration",
              "builtInRuleSets": ["clockwork.bcl.deterministic"],
              "builtInIncludeFamilies": ["Clock", "Random"],
              "builtInExcludeFamilies": ["Crypto"],
              "include": ["App*.dll"],
              "exclude": ["*.Tests.dll"],
              "targetRuntime": "10.0",
              "strongNameKeyPath": "app.snk"
            }
            """;

        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(doc);

        Assert.Equal(2, config.RuleSetPaths.Length);
        Assert.Equal(InstrumentationMode.RaceExploration, config.Mode);
        Assert.Equal(["clockwork.bcl.deterministic"], config.BuiltInRuleSetIds);
        Assert.Equal(["Clock", "Random"], config.BuiltInIncludeFamilies);
        Assert.Equal(["Crypto"], config.BuiltInExcludeFamilies);
        Assert.Equal(["App*.dll"], config.IncludePatterns);
        Assert.Equal(["*.Tests.dll"], config.ExcludePatterns);
        Assert.Equal(new Version(10, 0), config.TargetRuntime);
        Assert.Equal("app.snk", config.StrongNameKeyPath);
    }

    [Fact]
    public void ParsesBuiltInSelectionFields()
    {
        const string doc = """
            {
              "schemaVersion": 2,
              "builtInRuleSets": ["clockwork.bcl.deterministic"],
              "builtInIncludeFamilies": ["Clock", "Random"],
              "builtInExcludeFamilies": ["Crypto"]
            }
            """;

        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(doc);

        Assert.Equal(["clockwork.bcl.deterministic"], config.BuiltInRuleSetIds);
        Assert.Equal(["Clock", "Random"], config.BuiltInIncludeFamilies);
        Assert.Equal(["Crypto"], config.BuiltInExcludeFamilies);
    }

    [Fact]
    public void BuiltInDefaultsAreEmpty()
    {
        InstrumentationConfiguration config =
            InstrumentationConfigurationLoader.Parse("""{ "schemaVersion": 2, "ruleSets": ["r.json"] }""");

        Assert.True(config.BuiltInRuleSetIds.IsDefaultOrEmpty);
    }

    [Fact]
    public void ResolvesRelativePathsAgainstBaseDirectory()
    {
        string baseDir = OperatingSystem.IsWindows() ? @"C:\proj\cfg" : "/proj/cfg";
        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(
            """{ "schemaVersion": 2, "ruleSets": ["rules/clock.json"], "strongNameKeyPath": "keys/app.snk" }""",
            baseDir);

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "rules/clock.json")), config.RuleSetPaths[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "keys/app.snk")), config.StrongNameKeyPath);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "targetRuntime": "abc" }""", "not a valid version")]
    [InlineData("""{ "schemaVersion": 2, "ruleSets": "single" }""", "must be an array")]
    [InlineData("""{ "schemaVersion": 99 }""", "unsupported schemaVersion")]
    public void RejectsMalformedConfigurations(string json, string fragment)
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(() => InstrumentationConfigurationLoader.Parse(json));
        Assert.Contains(fragment, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresSchemaVersion()
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse("""{ "ruleSets": ["r.json"] }"""));

        Assert.Equal("configuration: required property 'schemaVersion' is missing.", ex.Message);
    }

    [Fact]
    public void RejectsSchemaVersionOne()
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse("""{ "schemaVersion": 1 }"""));

        Assert.Equal(
            "configuration declares unsupported schemaVersion 1; this tool supports version 2.",
            ex.Message);
    }

    [Theory]
    [InlineData("strictBuiltIns")]
    [InlineData("cryptoRandomnessPolicy")]
    [InlineData("rewritePolicy")]
    [InlineData("rewriteFallback")]
    [InlineData("excludeFrameworkAssemblies")]
    [InlineData("instrumentDependencies")]
    [InlineData("readyToRunPolicy")]
    [InlineData("strongNamePolicy")]
    public void RejectsRemovedRootProperties(string propertyName)
    {
        string json = $$"""{ "schemaVersion": 2, "{{propertyName}}": true }""";

        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(json, sourceName: "clockwork.config.json"));

        Assert.Equal(
            $"configuration 'clockwork.config.json': unknown property '{propertyName}'.",
            ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("instrumentDependecies")]
    [InlineData("SchemaVersion")]
    [InlineData("arbitrary")]
    public void RejectsUnknownAndMisspelledRootProperties(string propertyName)
    {
        string json = $$"""{ "schemaVersion": 2, "{{propertyName}}": null }""";

        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(json));

        Assert.Equal($"configuration: unknown property '{propertyName}'.", ex.Message);
    }

    [Fact]
    public void ValidatesRootSchemaBeforeReadingPropertyValues()
    {
        const string json = """{ "schemaVersion": "not-an-integer", "removed": true }""";

        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(json));

        Assert.Equal("configuration: unknown property 'removed'.", ex.Message);
    }

    [Fact]
    public void RejectsDuplicateRootPropertiesBeforeReadingPropertyValues()
    {
        const string json = """{ "schemaVersion": "not-an-integer", "schemaVersion": 2 }""";

        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(json));

        Assert.Equal("configuration: property 'schemaVersion' is specified more than once.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void InvalidJsonPreservesJsonException()
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(
                """{ "schemaVersion": 2, "mode": """,
                sourceName: "clockwork.config.json"));

        Assert.StartsWith(
            "configuration 'clockwork.config.json' is not valid JSON:",
            ex.Message,
            StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void SignatureIsStableAndChangeSensitive()
    {
        var baseConfig = new InstrumentationConfiguration { IncludePatterns = ["A*.dll"] };
        var same = new InstrumentationConfiguration { IncludePatterns = ["A*.dll"] };
        var different = baseConfig with { TargetRuntime = new Version(10, 0) };
        var raceExploration = baseConfig with { Mode = InstrumentationMode.RaceExploration };

        Assert.Equal(baseConfig.ComputeSignature(), same.ComputeSignature());
        Assert.NotEqual(baseConfig.ComputeSignature(), different.ComputeSignature());
        Assert.NotEqual(baseConfig.ComputeSignature(), raceExploration.ComputeSignature());
    }

    [Fact]
    public void EveryEffectiveJsonPropertyChangesTheSignature()
    {
        (string Baseline, string Changed)[] cases =
        [
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "ruleSets": ["rules.json"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "mode": "RaceExploration" }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "builtInRuleSets": ["clockwork.bcl.deterministic"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "builtInIncludeFamilies": ["Clock"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "builtInExcludeFamilies": ["Random"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "include": ["App*.dll"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "exclude": ["*.Tests.dll"] }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "targetRuntime": "10.0" }"""),
            ("""{ "schemaVersion": 2 }""", """{ "schemaVersion": 2, "strongNameKeyPath": "app.snk" }"""),
        ];

        foreach ((string baselineDocument, string changedDocument) in cases)
        {
            string baseline = InstrumentationConfigurationLoader.Parse(baselineDocument).ComputeSignature();
            string changed = InstrumentationConfigurationLoader.Parse(changedDocument).ComputeSignature();

            Assert.NotEqual(baseline, changed);
        }
    }

    [Fact]
    public void MaximumLengthConfigurationIdentifierIsAccepted()
    {
        string identifier = new('i', ClosureManifestLimits.MaxStringLength);
        string json = $$"""
            {
              "schemaVersion": 2,
              "builtInRuleSets": [{{JsonSerializer.Serialize(identifier)}}]
            }
            """;

        InstrumentationConfiguration configuration =
            InstrumentationConfigurationLoader.Parse(json);

        Assert.Equal(identifier, Assert.Single(configuration.BuiltInRuleSetIds));
    }

    [Fact]
    public void OverLimitConfigurationIdentifierIsRejectedAtAcceptance()
    {
        string identifier = new('i', ClosureManifestLimits.MaxStringLength + 1);
        string json = $$"""
            {
              "schemaVersion": 2,
              "builtInRuleSets": [{{JsonSerializer.Serialize(identifier)}}]
            }
            """;

        ConfigurationException exception = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse(json));

        Assert.Contains("builtInRuleSets", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exceeds", exception.Message, StringComparison.Ordinal);
    }
}
