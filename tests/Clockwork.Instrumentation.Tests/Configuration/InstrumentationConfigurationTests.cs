using Clockwork.Instrumentation.Configuration;

namespace Clockwork.Instrumentation.Tests.Configuration;

/// <summary>
/// Verifies the instrumentation-configuration loader: valid documents parse with defaults applied,
/// relative paths resolve against the configuration directory, the strong-name/ReadyToRun policies
/// enforce their invariants, and the signature is stable and change-sensitive.
/// </summary>
public sealed class InstrumentationConfigurationTests
{
    [Fact]
    public void AppliesDefaultsForMinimalDocument()
    {
        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse("""{ "ruleSets": ["r.json"] }""");

        Assert.True(config.ExcludeFrameworkAssemblies);
        Assert.True(config.RewriteDependencies);
        Assert.Equal(ReadyToRunPolicy.Reject, config.ReadyToRunPolicy);
        Assert.Equal(StrongNamePolicy.Fail, config.StrongNamePolicy);
        Assert.Null(config.TargetRuntime);
        Assert.Single(config.RuleSetPaths);
    }

    [Fact]
    public void ParsesAllFields()
    {
        const string doc = """
            {
              "schemaVersion": 1,
              "ruleSets": ["a.json", "b.json"],
              "include": ["App*.dll"],
              "exclude": ["*.Tests.dll"],
              "excludeFrameworkAssemblies": false,
              "rewriteDependencies": false,
              "targetRuntime": "10.0",
              "readyToRunPolicy": "StripToIL",
              "strongNamePolicy": "ReSign",
              "strongNameKeyPath": "app.snk"
            }
            """;

        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(doc);

        Assert.Equal(2, config.RuleSetPaths.Length);
        Assert.Equal(["App*.dll"], config.IncludePatterns);
        Assert.Equal(["*.Tests.dll"], config.ExcludePatterns);
        Assert.False(config.ExcludeFrameworkAssemblies);
        Assert.False(config.RewriteDependencies);
        Assert.Equal(new Version(10, 0), config.TargetRuntime);
        Assert.Equal(ReadyToRunPolicy.StripToIL, config.ReadyToRunPolicy);
        Assert.Equal(StrongNamePolicy.ReSign, config.StrongNamePolicy);
    }

    [Fact]
    public void ParsesBuiltInSelectionFields()
    {
        const string doc = """
            {
              "schemaVersion": 1,
              "builtInRuleSets": ["clockwork.bcl.deterministic"],
              "builtInIncludeFamilies": ["Clock", "Random"],
              "builtInExcludeFamilies": ["Crypto"],
              "strictBuiltIns": false
            }
            """;

        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(doc);

        Assert.Equal(["clockwork.bcl.deterministic"], config.BuiltInRuleSetIds);
        Assert.Equal(["Clock", "Random"], config.BuiltInIncludeFamilies);
        Assert.Equal(["Crypto"], config.BuiltInExcludeFamilies);
        Assert.False(config.StrictBuiltIns);
    }

    [Fact]
    public void BuiltInDefaultsAreEmptyAndStrict()
    {
        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse("""{ "ruleSets": ["r.json"] }""");

        Assert.True(config.BuiltInRuleSetIds.IsDefaultOrEmpty);
        Assert.True(config.StrictBuiltIns);
    }

    [Fact]
    public void ResolvesRelativePathsAgainstBaseDirectory()
    {
        string baseDir = OperatingSystem.IsWindows() ? @"C:\proj\cfg" : "/proj/cfg";
        InstrumentationConfiguration config = InstrumentationConfigurationLoader.Parse(
            """{ "ruleSets": ["rules/clock.json"], "strongNamePolicy": "ReSign", "strongNameKeyPath": "keys/app.snk" }""",
            baseDir);

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "rules/clock.json")), config.RuleSetPaths[0]);
        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, "keys/app.snk")), config.StrongNameKeyPath);
    }

    [Fact]
    public void ReSignRequiresKey()
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(
            () => InstrumentationConfigurationLoader.Parse("""{ "ruleSets": ["r.json"], "strongNamePolicy": "ReSign" }"""));
        Assert.Contains("strongNameKeyPath", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "readyToRunPolicy": "Wat" }""", "not one of")]
    [InlineData("""{ "targetRuntime": "abc" }""", "not a valid version")]
    [InlineData("""{ "ruleSets": "single" }""", "must be an array")]
    [InlineData("""{ "schemaVersion": 99 }""", "unsupported schemaVersion")]
    public void RejectsMalformedConfigurations(string json, string fragment)
    {
        ConfigurationException ex = Assert.Throws<ConfigurationException>(() => InstrumentationConfigurationLoader.Parse(json));
        Assert.Contains(fragment, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureIsStableAndChangeSensitive()
    {
        var baseConfig = new InstrumentationConfiguration { IncludePatterns = ["A*.dll"] };
        var same = new InstrumentationConfiguration { IncludePatterns = ["A*.dll"] };
        var different = baseConfig with { RewriteDependencies = false };

        Assert.Equal(baseConfig.ComputeSignature(), same.ComputeSignature());
        Assert.NotEqual(baseConfig.ComputeSignature(), different.ComputeSignature());
    }
}
