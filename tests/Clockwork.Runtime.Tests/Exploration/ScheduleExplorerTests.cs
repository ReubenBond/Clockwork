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
        ScheduleExplorationResult first = Explore(ExploreConfiguration(maxIterations: 4, stopOnFirstFailure: false), NoOpScenario);
        ScheduleExplorationResult second = Explore(ExploreConfiguration(maxIterations: 4, stopOnFirstFailure: false), NoOpScenario);

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
            ExploreConfiguration(maxIterations: 8, stopOnFirstFailure: true),
            RaceScenario);

        ScheduleExplorationIteration failure = Assert.Single(
            result.Iterations,
            static iteration => iteration.IsFailure);
        Assert.Equal(ExplorationTerminationReason.FirstFailure, result.TerminationReason);
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
            ExploreConfiguration(maxIterations: 32, stopOnFirstFailure: true),
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
        ScheduleExplorationConfiguration configuration = ExploreConfiguration(
            maxIterations: 20,
            stopOnFirstFailure: false) with
        {
            MaxFailures = 3,
        };

        ScheduleExplorationResult result = Explore(configuration, RaceScenario);

        Assert.Equal(ExplorationTerminationReason.FailureLimit, result.TerminationReason);
        Assert.Equal(3, result.FailureCount);
        Assert.Equal(3, result.OutcomeCounts[ReplayTerminationKind.RaceDetected]);
        Assert.Single(result.RetainedFailures);
    }

    [Fact]
    public void ParallelExplorationIsRejectedUntilIsolationIsProven()
    {
        ScheduleExplorationConfiguration configuration = ExploreConfiguration(2, true) with
        {
            Parallelism = 2,
        };

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => Explore(configuration, NoOpScenario));

        Assert.Contains("Parallelism=1", exception.Message, StringComparison.Ordinal);
    }

    private static ScheduleExplorationResult Explore(
        ScheduleExplorationConfiguration configuration,
        Action<ControlledOperationScheduler> scenario) =>
        ScheduleExplorer.Explore(configuration, scenario, TestContext.Current.CancellationToken);

    private static ScheduleExplorationConfiguration ExploreConfiguration(
        int maxIterations,
        bool stopOnFirstFailure) => new()
    {
        RootSeed = 777,
        FirstScheduleSeed = 100,
        MaxIterations = maxIterations,
        MaxStepsPerIteration = 10_000,
        StopOnFirstFailure = stopOnFirstFailure,
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
