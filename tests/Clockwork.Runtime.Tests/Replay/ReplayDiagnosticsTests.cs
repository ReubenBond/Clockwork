using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Replay;

public sealed class ReplayDiagnosticsTests
{
    [Fact]
    public void DeadlockTraceRendersStableWaitGraphAndCanonicalJson()
    {
        ReplayExecutionResult execution = ReplayRunner.Record(
            new ReplayRunConfiguration
            {
                RootSeed = 88,
                SchedulingPolicy = ReplaySchedulingPolicy.RoundRobin,
                IncludeDiagnosticMessages = true,
            },
            DeadlockScenario,
            TestContext.Current.CancellationToken);

        string first = ReplayTraceRenderer.RenderText(execution.Artifact);
        ReplayArtifact roundTripped = ReplayArtifactSerializer.Deserialize(
            ReplayArtifactSerializer.Serialize(execution.Artifact));
        string second = ReplayTraceRenderer.RenderText(roundTripped);

        Assert.Equal(first, second);
        Assert.Contains("Deadlock cycle 1:", first, StringComparison.Ordinal);
        Assert.Contains("op1 -> res2 -> op2", first, StringComparison.Ordinal);
        Assert.Contains("Resources:", first, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', first);
    }

    [Fact]
    public void DefaultDiagnosticsExcludeCallerDescriptionsAndSourcePaths()
    {
        ReplayExecutionResult execution = ReplayRunner.Record(
            new ReplayRunConfiguration
            {
                RootSeed = 89,
                SchedulingPolicy = ReplaySchedulingPolicy.RoundRobin,
            },
            static scheduler => scheduler.Schedule("secret-work-description", static () => { }),
            TestContext.Current.CancellationToken);

        string json = ReplayArtifactSerializer.ToJson(execution.Artifact);

        Assert.DoesNotContain("secret-work-description", json, StringComparison.Ordinal);
        Assert.All(execution.Artifact.Diagnostics.Operations, static operation => Assert.Null(operation.Description));
    }

    private static void DeadlockScenario(ControlledOperationScheduler scheduler)
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
}
