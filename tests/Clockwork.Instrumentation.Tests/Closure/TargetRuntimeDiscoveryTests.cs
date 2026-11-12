using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Closure;

public sealed class TargetRuntimeDiscoveryTests : IDisposable
{
    private readonly string _root = TestArtifacts.CreateUnique("cwr-target-runtime");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "11.0.0" } } }""", "11.0")]
    [InlineData(
        """
        {
          // The host accepts comments and trailing commas.
          "runtimeOptions": {
            "rollForward": "LatestMajor",
            "frameworks": [
              { "name": "Microsoft.AspNetCore.App", "version": "12.0.0" },
              { "name": "Microsoft.NETCore.App", "version": "11.0.3" },
            ],
          },
        }
        """,
        "11.0")]
    [InlineData("""{ "runtimeOptions": { "framework": { "name": "Microsoft.AspNetCore.App", "version": "11.0.1" } } }""", "11.0")]
    [InlineData(
        """{ "runtimeOptions": { "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "11.0.0-rc.2.26504.105" } ] } }""",
        "11.0")]
    public void DerivesTheRuntimeTheEntryRunsOn(string runtimeConfig, string expected)
    {
        File.WriteAllText(Path.Combine(_root, "App.runtimeconfig.json"), runtimeConfig);

        (Version targetRuntime, string origin) = TargetRuntimeDiscovery.Resolve(new InstrumentationConfiguration(), Plan("App.dll"));

        Assert.Equal(Version.Parse(expected), targetRuntime);
        Assert.Equal("from 'App.runtimeconfig.json'", origin);
    }

    [Fact]
    public void ConfiguredTargetRuntimeWins()
    {
        (Version targetRuntime, string origin) = TargetRuntimeDiscovery.Resolve(
            new InstrumentationConfiguration { TargetRuntime = new Version(12, 0) },
            Plan(entry: null));

        Assert.Equal(new Version(12, 0), targetRuntime);
        Assert.Equal("configured", origin);
    }

    [Theory]
    [InlineData(null, null, "no entry assembly")]
    [InlineData("App.dll", null, "'App.runtimeconfig.json' was not found")]
    [InlineData("App.dll", "{ \"runtimeOptions\": {} }", "references none of Microsoft.NETCore.App")]
    [InlineData("App.dll", "{ \"runtimeOptions\": { \"framework\": { \"name\": \"Microsoft.NETCore.App\" } } }", "which is not a .NET version")]
    [InlineData("App.dll", "{", "is not valid JSON")]
    public void FailsWhenTheClosureNamesNoRuntime(string? entry, string? runtimeConfig, string reason)
    {
        if (runtimeConfig is not null)
        {
            File.WriteAllText(Path.Combine(_root, "App.runtimeconfig.json"), runtimeConfig);
        }

        ClosureException exception = Assert.Throws<ClosureException>(
            () => TargetRuntimeDiscovery.Resolve(new InstrumentationConfiguration(), Plan(entry)));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
        Assert.Contains("ClockworkTargetRuntime", exception.Message, StringComparison.Ordinal);
    }

    private ClosurePlan Plan(string? entry) => new(_root, entry, []);
}
