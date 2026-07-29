using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;
using Clockwork.Testing;

namespace Clockwork.Testing.Tests;

[Collection("Replay test environment")]
public sealed class ReplayTestFixtureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "clockwork-testing",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public void ReusesStableSimulationTestIdentitySeed()
    {
        var fixture = new ReplayTestFixture(new ReplayTestConfiguration
        {
            TestClassName = "TestClass",
            TestMethodName = "TestMethod",
        });

        Assert.Equal(322282660, fixture.TestIdentitySeed);
    }

    [Fact]
    public void FailedTestWritesArtifactAndReplayInstructions()
    {
        ReplayTestResult result = CreateFixture("WritesArtifact").Run(
            static scheduler => scheduler.Schedule("fault", static () => throw new KnownTestFailure()),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));
        Assert.Contains(ReplayTestEnvironment.Artifact, result.ToFailureMessage(), StringComparison.Ordinal);
        Assert.Contains("clockwork replay", result.GetReplayCommand("tests.dll", "Tests.Scenario"), StringComparison.Ordinal);
        ReplayTestFailureException exception = Assert.Throws<ReplayTestFailureException>(result.ThrowIfFailed);
        Assert.Same(result, exception.Result);
    }

    [Fact]
    public void SuccessfulTestDoesNotWriteArtifactByDefault()
    {
        ReplayTestResult result = CreateFixture("Success").Run(
            static scheduler => scheduler.Schedule("complete", static () => { }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccessful);
        Assert.Null(result.ArtifactPath);
    }

    [Fact]
    public void EnvironmentArtifactReplaysExactFailure()
    {
        ReplayTestFixture fixture = CreateFixture("EnvironmentReplay");
        ReplayTestResult recorded = fixture.Run(
            static scheduler => scheduler.Schedule("fault", static () => throw new KnownTestFailure()),
            TestContext.Current.CancellationToken);
        string? previous = Environment.GetEnvironmentVariable(ReplayTestEnvironment.Artifact);
        try
        {
            Environment.SetEnvironmentVariable(ReplayTestEnvironment.Artifact, recorded.ArtifactPath);

            ReplayTestResult replayed = fixture.Run(
                static scheduler => scheduler.Schedule("fault", static () => throw new KnownTestFailure()),
                TestContext.Current.CancellationToken);

            Assert.True(replayed.Execution.Reproduced);
            Assert.Equal(recorded.ArtifactPath, replayed.ArtifactPath);
            Assert.Equal(recorded.RootSeed, replayed.RootSeed);
            Assert.Equal(recorded.ScheduleSeed, replayed.ScheduleSeed);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReplayTestEnvironment.Artifact, previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ReplayTestFixture CreateFixture(string method) => new(new ReplayTestConfiguration
    {
        TestClassName = nameof(ReplayTestFixtureTests),
        TestMethodName = method,
        ArtifactDirectory = _root,
    });

    private sealed class KnownTestFailure : Exception;
}

[CollectionDefinition("Replay test environment", DisableParallelization = true)]
public sealed class ReplayTestEnvironmentGroup;
