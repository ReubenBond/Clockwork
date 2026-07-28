using Clockwork.Runtime.Exploration;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Exploration;

public sealed class ReplayTraceMinimizerTests
{
    [Fact]
    public void MinimizerShrinksKnownTraceAndPreservesFailureIdentity()
    {
        ReplayExecutionResult recorded = RecordLongFaultTrace();
        Func<ReplayArtifact, ReplayFailureObservation> predicate = ReplayFailurePredicates.ForScenario(
            ReplayCompatibilityRequirements.Current(),
            FaultScenario,
            maxSteps: 10_000,
            TestContext.Current.CancellationToken);

        ReplayMinimizationResult result = ReplayTraceMinimizer.Minimize(
            recorded.Artifact,
            new ReplayMinimizationConfiguration { MaxAttempts = 200 },
            predicate);

        Assert.True(result.Verified);
        Assert.True(result.MinimizedDecisionCount < result.OriginalDecisionCount);
        Assert.Equal(1, result.MinimizedDecisionCount);
        Assert.Equal(
            recorded.Artifact.Outcome.FailureIdentity,
            result.MinimizedArtifact.Outcome.FailureIdentity);
        Assert.Contains(result.Progress, static progress => progress.Accepted);
    }

    [Fact]
    public void DivergentCandidatesAreRejectedWithoutLosingFirstDivergenceSemantics()
    {
        ReplayExecutionResult recorded = RecordLongFaultTrace();
        Func<ReplayArtifact, ReplayFailureObservation> predicate = ReplayFailurePredicates.ForScenario(
            ReplayCompatibilityRequirements.Current(),
            FaultScenario,
            maxSteps: 10_000,
            TestContext.Current.CancellationToken);

        ReplayArtifact divergent = recorded.Artifact with
        {
            Decisions =
            [
                recorded.Artifact.Decisions[0] with { SelectedResult = "999" },
            ],
        };
        ReplayFailureObservation observation = predicate(divergent);

        Assert.False(observation.Reproduced);
        Assert.StartsWith("divergence:", observation.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AttemptBoundIsDeterministic()
    {
        ReplayExecutionResult recorded = RecordLongFaultTrace();
        Func<ReplayArtifact, ReplayFailureObservation> predicate = ReplayFailurePredicates.ForScenario(
            ReplayCompatibilityRequirements.Current(),
            FaultScenario,
            maxSteps: 10_000,
            TestContext.Current.CancellationToken);

        ReplayMinimizationResult first = ReplayTraceMinimizer.Minimize(
            recorded.Artifact,
            new ReplayMinimizationConfiguration { MaxAttempts = 3 },
            predicate);
        ReplayMinimizationResult second = ReplayTraceMinimizer.Minimize(
            recorded.Artifact,
            new ReplayMinimizationConfiguration { MaxAttempts = 3 },
            predicate);

        Assert.Equal(3, first.Attempts);
        Assert.Equal(
            first.Progress.Select(static progress => (progress.Action, progress.Accepted)),
            second.Progress.Select(static progress => (progress.Action, progress.Accepted)));
    }

    private static ReplayExecutionResult RecordLongFaultTrace()
    {
        for (var scheduleSeed = 1; scheduleSeed <= 100; scheduleSeed++)
        {
            ReplayExecutionResult execution = ReplayRunner.Record(
                new ReplayRunConfiguration
                {
                    RootSeed = 55,
                    SchedulingPolicy = ReplaySchedulingPolicy.SeededRandom,
                    ScheduleSeed = scheduleSeed,
                    MaxSteps = 10_000,
                },
                FaultScenario,
                TestContext.Current.CancellationToken);
            if (execution.Artifact.Decisions.Count >= 4)
            {
                return execution;
            }
        }

        throw new InvalidOperationException("The deterministic seed corpus did not produce a long fault trace.");
    }

    private static void FaultScenario(ControlledOperationScheduler scheduler)
    {
        scheduler.Schedule(
            "background-one",
            () =>
            {
                for (var index = 0; index < 5; index++)
                {
                    scheduler.Yield();
                }
            });
        scheduler.Schedule(
            "background-two",
            () =>
            {
                for (var index = 0; index < 5; index++)
                {
                    scheduler.Yield();
                }
            });
        scheduler.Schedule("fault", static () => throw new KnownFailureException());
    }

    private sealed class KnownFailureException : Exception;
}
