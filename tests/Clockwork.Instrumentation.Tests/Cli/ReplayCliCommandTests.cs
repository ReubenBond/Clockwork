using System.Text.Json;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;
using Clockwork.Tool;

namespace Clockwork.Instrumentation.Tests.Cli;

public sealed class ReplayCliCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "clockwork-replay-cli",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public void RunReplayAndTraceShowRoundTripExplicitScenario()
    {
        string artifact = Path.Combine(_root, "success.cwr.json");

        (ExitCode runCode, string runOutput, string runError) = Invoke(
            "run",
            "--assembly", TestAssembly,
            "--scenario-type", typeof(SuccessScenario).FullName!,
            "--artifact", artifact,
            "--seed", "42",
            "--schedule-seed", "7",
            "--json");
        (ExitCode replayCode, string replayOutput, string replayError) = Invoke(
            "replay",
            artifact,
            "--assembly", TestAssembly,
            "--scenario-type", typeof(SuccessScenario).FullName!,
            "--json");
        (ExitCode traceCode, string traceOutput, string traceError) = Invoke(
            "trace", "show", artifact);

        Assert.Equal(ExitCode.Success, runCode);
        Assert.Equal(ExitCode.Success, replayCode);
        Assert.Equal(ExitCode.Success, traceCode);
        Assert.True(File.Exists(artifact));
        Assert.Empty(runError);
        Assert.Empty(replayError);
        Assert.Empty(traceError);
        Assert.Equal("Completed", JsonDocument.Parse(runOutput).RootElement.GetProperty("outcome").GetString());
        Assert.True(JsonDocument.Parse(replayOutput).RootElement.GetProperty("reproduced").GetBoolean());
        Assert.Contains("Clockwork replay clockwork.replay/v1", traceOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void RunReturnsClassifiedFailureAndWritesArtifact()
    {
        string artifact = Path.Combine(_root, "fault.cwr.json");

        (ExitCode code, string output, string error) = Invoke(
            "run",
            "--assembly", TestAssembly,
            "--scenario-type", typeof(FaultScenario).FullName!,
            "--artifact", artifact,
            "--seed", "43",
            "--schedule-seed", "8");

        Assert.Equal(ExitCode.ExecutionFailure, code);
        Assert.Contains("outcome: Faulted", output, StringComparison.Ordinal);
        Assert.Empty(error);
        Assert.Equal(ReplayTerminationKind.Faulted, ReplayArtifactSerializer.Read(artifact).Outcome.Kind);
    }

    [Fact]
    public void ExploreWritesRetainedRaceArtifact()
    {
        string outputDirectory = Path.Combine(_root, "explore");

        (ExitCode code, string output, string error) = Invoke(
            "explore",
            "--assembly", TestAssembly,
            "--scenario-type", typeof(RaceScenario).FullName!,
            "--output", outputDirectory,
            "--seed", "44",
            "--schedule-seed", "9",
            "--count", "4",
            "--stop-on-first",
            "--json");

        Assert.Equal(ExitCode.ExecutionFailure, code);
        Assert.Empty(error);
        Assert.Equal(1, JsonDocument.Parse(output).RootElement.GetProperty("failures").GetInt32());
        string retained = Assert.Single(Directory.GetFiles(outputDirectory, "*.cwr.json"));
        Assert.Equal(ReplayTerminationKind.RaceDetected, ReplayArtifactSerializer.Read(retained).Outcome.Kind);
    }

    [Fact]
    public void MinimizeWritesVerifiedSmallerArtifact()
    {
        string artifact = RecordLongFaultArtifact();
        ReplayArtifact original = ReplayArtifactSerializer.Read(artifact);
        string minimizedPath = Path.Combine(_root, "minimized.cwr.json");

        (ExitCode code, string output, string error) = Invoke(
            "minimize",
            artifact,
            "--assembly", TestAssembly,
            "--scenario-type", typeof(LongFaultScenario).FullName!,
            "--output", minimizedPath,
            "--max-attempts", "200",
            "--json");

        ReplayArtifact minimized = ReplayArtifactSerializer.Read(minimizedPath);
        Assert.Equal(ExitCode.Success, code);
        Assert.Empty(error);
        Assert.True(JsonDocument.Parse(output).RootElement.GetProperty("verified").GetBoolean());
        Assert.True(minimized.Decisions.Count < original.Decisions.Count);
        Assert.Equal(original.Outcome.FailureIdentity, minimized.Outcome.FailureIdentity);
    }

    [Fact]
    public void MissingExplicitScenarioTypeIsUsageError()
    {
        (ExitCode code, _, string error) = Invoke(
            "run",
            "--assembly", TestAssembly,
            "--artifact", Path.Combine(_root, "missing.cwr.json"),
            "--seed", "1");

        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("--scenario-type", error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    public sealed class SuccessScenario : IReplayScenario
    {
        public void Configure(ControlledOperationScheduler scheduler)
        {
            scheduler.Schedule("one", scheduler.Yield);
            scheduler.Schedule("two", static () => { });
        }
    }

    public sealed class FaultScenario : IReplayScenario
    {
        public void Configure(ControlledOperationScheduler scheduler) =>
            scheduler.Schedule("fault", static () => throw new KnownCliFailureException());
    }

    public sealed class RaceScenario : IReplayScenario
    {
        public void Configure(ControlledOperationScheduler scheduler)
        {
            var target = new object();
            scheduler.Schedule(
                "one",
                () => RaceInstrumentation.WriteInstance(
                    target, "State.Value", "CliRace::One", 1, sourceFile: null, sourceLine: -1));
            scheduler.Schedule(
                "two",
                () => RaceInstrumentation.WriteInstance(
                    target, "State.Value", "CliRace::Two", 2, sourceFile: null, sourceLine: -1));
        }
    }

    public sealed class LongFaultScenario : IReplayScenario
    {
        public void Configure(ControlledOperationScheduler scheduler)
        {
            scheduler.Schedule("background-one", () => YieldMany(scheduler));
            scheduler.Schedule("background-two", () => YieldMany(scheduler));
            scheduler.Schedule("fault", static () => throw new KnownCliFailureException());
        }

        private static void YieldMany(ControlledOperationScheduler scheduler)
        {
            for (var index = 0; index < 5; index++)
            {
                scheduler.Yield();
            }
        }
    }

    private static string TestAssembly => typeof(ReplayCliCommandTests).Assembly.Location;

    private string RecordLongFaultArtifact()
    {
        for (var seed = 1; seed <= 100; seed++)
        {
            string artifact = Path.Combine(_root, $"long-{seed}.cwr.json");
            (ExitCode code, _, _) = Invoke(
                "run",
                "--assembly", TestAssembly,
                "--scenario-type", typeof(LongFaultScenario).FullName!,
                "--artifact", artifact,
                "--seed", "45",
                "--schedule-seed", seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(ExitCode.ExecutionFailure, code);
            if (ReplayArtifactSerializer.Read(artifact).Decisions.Count >= 4)
            {
                return artifact;
            }
        }

        throw new InvalidOperationException("The CLI seed corpus did not produce a long trace.");
    }

    private static (ExitCode Code, string Output, string Error) Invoke(params string[] args)
    {
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using var error = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        ExitCode code = Program.Run(args, output, error);
        return (code, output.ToString().Trim(), error.ToString().Trim());
    }

    private sealed class KnownCliFailureException : Exception;
}
