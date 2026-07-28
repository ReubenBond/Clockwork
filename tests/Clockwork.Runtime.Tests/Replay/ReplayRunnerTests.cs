using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Replay;

public sealed class ReplayRunnerTests
{
    [Fact]
    public void RecordAndReplaySuccessfulSeededSchedule()
    {
        var recordedOrder = new List<string>();
        ReplayRunConfiguration configuration = SeededConfiguration(scheduleSeed: 91);

        ReplayExecutionResult recorded = Record(
            configuration,
            scheduler => ScheduleYieldingPair(scheduler, recordedOrder));
        var replayedOrder = new List<string>();
        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            scheduler => ScheduleYieldingPair(scheduler, replayedOrder));

        Assert.Equal(ReplayRecordingState.Complete, recorded.Artifact.RecordingState);
        Assert.Equal(ReplayTerminationKind.Completed, recorded.Artifact.Outcome.Kind);
        Assert.NotEmpty(recorded.Artifact.Decisions);
        Assert.True(replayed.Reproduced);
        Assert.Equal(recordedOrder, replayedOrder);
    }

    [Fact]
    public void RecordAndReplayFaultByStableExceptionIdentity()
    {
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(3),
            static scheduler => scheduler.Schedule("fault", static () => throw new KnownReplayException()));

        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            static scheduler => scheduler.Schedule("fault", static () => throw new KnownReplayException()));

        Assert.Equal(ReplayTerminationKind.Faulted, recorded.Artifact.Outcome.Kind);
        Assert.Equal(typeof(KnownReplayException).FullName, recorded.Artifact.Outcome.FailureIdentity);
        Assert.Null(recorded.Artifact.Outcome.Diagnostic);
        Assert.True(replayed.Reproduced);
    }

    [Fact]
    public void RecordAndReplayCanceledOperation()
    {
        static void Scenario(ControlledOperationScheduler scheduler)
        {
            ControlledOperation operation = scheduler.Schedule("cancel", static () => { });
            scheduler.Cancel(operation);
        }

        ReplayExecutionResult recorded = Record(SeededConfiguration(4), Scenario);
        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            Scenario);

        Assert.Equal(ReplayTerminationKind.Canceled, recorded.Artifact.Outcome.Kind);
        Assert.True(replayed.Reproduced);
    }

    [Fact]
    public void RecordAndReplayVirtualTimeout()
    {
        static void Scenario(ControlledOperationScheduler scheduler)
        {
            ControlledResource resource = scheduler.CreateResource(ControlledResourceKind.Timer, "timeout");
            scheduler.Schedule(
                "waiter",
                () =>
                {
                    ControlledWaitOutcome outcome = scheduler.WaitOnResource(
                        resource,
                        TimeSpan.FromSeconds(5),
                        ControlledOperationPauseReason.ResourceWait("timeout"));
                    Assert.Equal(ControlledWaitOutcome.TimedOut, outcome);
                });
        }

        ReplayExecutionResult recorded = Record(SeededConfiguration(5), Scenario);
        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            Scenario);

        Assert.Contains(
            recorded.Artifact.Decisions,
            static decision => decision.SourceId == "resource-wait-resolution" &&
                decision.SelectedResult == nameof(ControlledWaitOutcome.TimedOut));
        Assert.True(replayed.Reproduced);
    }

    [Fact]
    public void RecordAndReplayDeadlockAtTerminalBoundary()
    {
        static void Scenario(ControlledOperationScheduler scheduler)
        {
            ControlledResource first = scheduler.CreateResource(ControlledResourceKind.Monitor, "first");
            ControlledResource second = scheduler.CreateResource(ControlledResourceKind.Monitor, "second");
            scheduler.Schedule(
                "one",
                () =>
                {
                    scheduler.MarkResourceOwner(first, scheduler.CurrentOperation);
                    scheduler.Yield();
                    scheduler.WaitOnResource(second, ControlledOperationPauseReason.ResourceWait("second"));
                });
            scheduler.Schedule(
                "two",
                () =>
                {
                    scheduler.MarkResourceOwner(second, scheduler.CurrentOperation);
                    scheduler.Yield();
                    scheduler.WaitOnResource(first, ControlledOperationPauseReason.ResourceWait("first"));
                });
        }

        ReplayRunConfiguration configuration = new()
        {
            RootSeed = 7,
            SchedulingPolicy = ReplaySchedulingPolicy.RoundRobin,
        };
        ReplayExecutionResult recorded = Record(configuration, Scenario);
        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            Scenario);

        Assert.Equal(ReplayTerminationKind.Deadlocked, recorded.Artifact.Outcome.Kind);
        Assert.StartsWith("deadlock:", recorded.Artifact.Outcome.FailureIdentity, StringComparison.Ordinal);
        Assert.True(replayed.Reproduced);
    }

    [Fact]
    public void SeededSchedulingRandomizesResourceWinnersAndReplaysThem()
    {
        var recordedWinners = new List<int>();
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(29),
            scheduler => ScheduleResourceContest(scheduler, recordedWinners));
        var replayedWinners = new List<int>();
        ReplayExecutionResult replayed = Replay(
            recorded.Artifact,
            scheduler => ScheduleResourceContest(scheduler, replayedWinners));

        Assert.Contains(
            recorded.Artifact.Decisions,
            static decision => decision.Kind == SimulationDecisionKind.ResourceWinner);
        Assert.Equal(recordedWinners, replayedWinners);
        Assert.True(replayed.Reproduced);
    }

    [Fact]
    public void TruncatedDecisionStreamFailsAtFirstMissingChoice()
    {
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(31),
            static scheduler => ScheduleYieldingPair(scheduler, []));
        ReplayDecision[] truncated = recorded.Artifact.Decisions.SkipLast(1).ToArray();
        ReplayArtifact artifact = recorded.Artifact with { Decisions = truncated };

        SimulationDecisionReplayMismatchException exception = Assert.Throws<SimulationDecisionReplayMismatchException>(
            () => Replay(
                artifact,
                static scheduler => ScheduleYieldingPair(scheduler, [])));

        Assert.Contains("exhausted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SurplusDecisionStreamFailsAtTerminalBoundary()
    {
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(37),
            static scheduler => ScheduleYieldingPair(scheduler, []));
        ReplayDecision extra = recorded.Artifact.Decisions[^1] with
        {
            Sequence = recorded.Artifact.Decisions.Count,
        };
        ReplayArtifact artifact = recorded.Artifact with
        {
            Decisions = [.. recorded.Artifact.Decisions, extra],
        };

        SimulationDecisionReplayMismatchException exception = Assert.Throws<SimulationDecisionReplayMismatchException>(
            () => Replay(
                artifact,
                static scheduler => ScheduleYieldingPair(scheduler, [])));

        Assert.Contains("remain unconsumed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentScenarioFailsWithExpectedAndActualDecisionContext()
    {
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(41),
            static scheduler => ScheduleYieldingPair(scheduler, []));

        SimulationDecisionReplayMismatchException exception = Assert.Throws<SimulationDecisionReplayMismatchException>(
            () => Replay(
                recorded.Artifact,
                static scheduler =>
                {
                    scheduler.Schedule("different-a", scheduler.Yield);
                    scheduler.Schedule("different-b", static () => { });
                    scheduler.Schedule("different-c", static () => { });
                }));

        Assert.True(exception.Expected is not null || exception.Message.Contains("recorded", StringComparison.Ordinal));
        Assert.Contains("Replay diverged", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalEntryFailureProducesExplicitAbortedPrefix()
    {
        ReplayExecutionResult recorded = Record(
            SeededConfiguration(43),
            static _ => throw new InvalidOperationException("external entry rejected"));

        Assert.Equal(ReplayRecordingState.Aborted, recorded.Artifact.RecordingState);
        Assert.Equal(ReplayTerminationKind.Aborted, recorded.Artifact.Outcome.Kind);
        Assert.Equal(typeof(InvalidOperationException).FullName, recorded.Artifact.Outcome.FailureIdentity);
        Assert.Throws<ReplayCompatibilityException>(
            () => Replay(
                recorded.Artifact,
                static _ => { }));
    }

    private static ReplayRunConfiguration SeededConfiguration(int scheduleSeed) => new()
    {
        RootSeed = 1234,
        SchedulingPolicy = ReplaySchedulingPolicy.SeededRandom,
        ScheduleSeed = scheduleSeed,
        MaxSteps = 10_000,
    };

    private static ReplayExecutionResult Record(
        ReplayRunConfiguration configuration,
        Action<ControlledOperationScheduler> scenario) =>
        ReplayRunner.Record(configuration, scenario, TestContext.Current.CancellationToken);

    private static ReplayExecutionResult Replay(
        ReplayArtifact artifact,
        Action<ControlledOperationScheduler> scenario) =>
        ReplayRunner.Replay(
            artifact,
            ReplayCompatibilityRequirements.Current(),
            scenario,
            maxSteps: 1_000_000,
            cancellationToken: TestContext.Current.CancellationToken);

    private static void ScheduleYieldingPair(
        ControlledOperationScheduler scheduler,
        List<string> order)
    {
        scheduler.Schedule(
            "first",
            () =>
            {
                order.Add("first:1");
                scheduler.Yield();
                order.Add("first:2");
            });
        scheduler.Schedule(
            "second",
            () =>
            {
                order.Add("second:1");
                scheduler.Yield();
                order.Add("second:2");
            });
    }

    private static void ScheduleResourceContest(
        ControlledOperationScheduler scheduler,
        List<int> winners)
    {
        ControlledResource resource = scheduler.CreateResource(ControlledResourceKind.Semaphore, "contest");
        for (var id = 1; id <= 3; id++)
        {
            int captured = id;
            scheduler.Schedule(
                $"waiter-{captured}",
                () =>
                {
                    scheduler.WaitOnResource(
                        resource,
                        ControlledOperationPauseReason.ResourceWait("contest"));
                    winners.Add(captured);
                });
        }

        scheduler.Schedule(
            "signaler",
            () =>
            {
                while (resource.WaiterCount < 3)
                {
                    scheduler.Yield();
                }

                scheduler.SignalOne(resource);
                scheduler.SignalOne(resource);
                scheduler.SignalOne(resource);
            });
    }

    private sealed class KnownReplayException : Exception;
}
