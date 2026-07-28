using Clockwork.Runtime.Exploration;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Exploration;

public sealed class ScheduleExplorerTests
{
    [Fact]
    public void IterationIdsAndSeedsAreDeterministic()
    {
        ScheduleExplorationResult first = Explore(ExploreOptions(maxIterations: 4, maxFailures: 1), NoOpScenario);
        ScheduleExplorationResult second = Explore(ExploreOptions(maxIterations: 4, maxFailures: 1), NoOpScenario);

        Assert.Equal(
            first.Iterations.Select(static iteration => (iteration.IterationId, iteration.ScheduleSeed)),
            second.Iterations.Select(static iteration => (iteration.IterationId, iteration.ScheduleSeed)));
        Assert.Equal([100, 101, 102, 103], first.Iterations.Select(static iteration => iteration.ScheduleSeed));
        Assert.All(first.Iterations, static iteration => Assert.Equal(777, iteration.Execution.Artifact.RootSeed));
    }

    [Fact]
    public void ExplorationFindsRaceWithinFixedBoundAndReplaysExactly()
    {
        ScheduleExplorationResult result = Explore(
            ExploreOptions(maxIterations: 8, maxFailures: 1),
            RaceScenario);

        ScheduleExplorationIteration failure = Assert.Single(
            result.Iterations,
            static iteration => iteration.IsFailure);
        Assert.Equal(ExplorationTerminationReason.FailureLimit, result.TerminationReason);
        Assert.Equal(ReplayTerminationKind.RaceDetected, failure.Execution.Artifact.Outcome.Kind);
        ReplayExecutionResult replay = ReplayRunner.Replay(
            failure.Execution.Artifact,
            ReplayCompatibilityRequirements.Current(),
            RaceScenario,
            maxSteps: 1_000_000,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(replay.Reproduced);
    }

    [Fact]
    public void ExplorationFindsDeadlockWithinFixedBoundAndReplaysExactly()
    {
        ScheduleExplorationResult result = Explore(
            ExploreOptions(maxIterations: 32, maxFailures: 1),
            ConditionalDeadlockScenario);

        ScheduleExplorationIteration failure = Assert.Single(
            result.Iterations,
            static iteration => iteration.IsFailure);
        Assert.Equal(ReplayTerminationKind.Deadlocked, failure.Execution.Artifact.Outcome.Kind);
        ReplayExecutionResult replay = ReplayRunner.Replay(
            failure.Execution.Artifact,
            ReplayCompatibilityRequirements.Current(),
            ConditionalDeadlockScenario,
            maxSteps: 1_000_000,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(replay.Reproduced);
    }

    [Fact]
    public void FailureLimitAndAggregateCountsAreStable()
    {
        ScheduleExplorationOptions configuration = ExploreOptions(maxIterations: 20, maxFailures: 3);

        ScheduleExplorationResult result = Explore(configuration, RaceScenario);

        Assert.Equal(ExplorationTerminationReason.FailureLimit, result.TerminationReason);
        Assert.Equal(3, result.FailureCount);
        Assert.Equal(3, result.OutcomeCounts[ReplayTerminationKind.RaceDetected]);
        Assert.Single(result.RetainedFailures);
    }

    private static ScheduleExplorationResult Explore(
        ScheduleExplorationOptions options,
        Action<ControlledOperationScheduler> scenario) =>
        ScheduleExplorer.Explore(options, scenario, TestContext.Current.CancellationToken);

    private static ScheduleExplorationOptions ExploreOptions(
        int maxIterations,
        int maxFailures) => new()
    {
        RootSeed = 777,
        FirstScheduleSeed = 100,
        MaxIterations = maxIterations,
        MaxStepsPerIteration = 10_000,
        MaxFailures = maxFailures,
    };

    private static void NoOpScenario(ControlledOperationScheduler scheduler) =>
        scheduler.Schedule("complete", static () => { });

    private static void RaceScenario(ControlledOperationScheduler scheduler)
    {
        var target = new object();
        scheduler.Schedule(
            "writer-one",
            () => RaceInstrumentation.WriteInstance(
                target,
                "Counter.Value",
                "Scenario::WriterOne",
                1,
                sourceFile: null,
                sourceLine: -1));
        scheduler.Schedule(
            "writer-two",
            () => RaceInstrumentation.WriteInstance(
                target,
                "Counter.Value",
                "Scenario::WriterTwo",
                2,
                sourceFile: null,
                sourceLine: -1));
    }

    private static void ConditionalDeadlockScenario(ControlledOperationScheduler scheduler)
    {
        ControlledResource first = scheduler.CreateResource(ControlledResourceKind.Monitor, "first");
        ControlledResource second = scheduler.CreateResource(ControlledResourceKind.Monitor, "second");
        scheduler.Schedule(
            "one",
            () =>
            {
                scheduler.MarkResourceOwner(first, scheduler.CurrentOperation);
                scheduler.Yield();
                if (second.Owner is not null)
                {
                    scheduler.WaitOnResource(second, ControlledOperationPauseReason.ResourceWait("second"));
                }
            });
        scheduler.Schedule(
            "two",
            () =>
            {
                scheduler.MarkResourceOwner(second, scheduler.CurrentOperation);
                scheduler.Yield();
                if (first.Owner is not null)
                {
                    scheduler.WaitOnResource(first, ControlledOperationPauseReason.ResourceWait("first"));
                }
            });
    }
}
