using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Scheduling.Resources;

/// <summary>
/// Coverage of wait-for graph construction and deadlock detection: true resource-ownership cycles of
/// length 2 and 3 are reported with full deterministic metadata, while paused-until-time,
/// externally-completable, progressing, and quiescent states are correctly distinguished and never
/// falsely flagged as deadlocked.
/// </summary>
public sealed class ControlledDeadlockDetectionTests
{
    private static ControlledOperationPauseReason Reason(string tag) => ControlledOperationPauseReason.ResourceWait(tag);

    [Fact]
    public void TwoOperationOwnershipCycleIsReportedAsDeadlock()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ra = scheduler.CreateResource(ControlledResourceKind.Monitor, "A");
        var rb = scheduler.CreateResource(ControlledResourceKind.Monitor, "B");

        var opA = scheduler.Schedule("A", () => scheduler.WaitOnResource(rb, Reason("B")));
        var opB = scheduler.Schedule("B", () => scheduler.WaitOnResource(ra, Reason("A")));
        scheduler.MarkResourceOwner(ra, opA);
        scheduler.MarkResourceOwner(rb, opB);

        scheduler.Drain();

        var report = scheduler.DetectDeadlock();
        Assert.Equal(ControlledLivenessState.Deadlocked, report.Liveness);
        Assert.True(report.IsDeadlocked);
        var cycle = Assert.Single(report.Cycles);
        Assert.Equal(2, cycle.Entries.Count);

        // Rotated to start at the smallest operation id (opA).
        Assert.Equal(opA.Id, cycle.Entries[0].OperationId);
        Assert.Equal(rb.Id, cycle.Entries[0].ResourceId);
        Assert.Equal(opB.Id, cycle.Entries[0].OwnerId);
        Assert.Equal(opB.Id, cycle.Entries[1].OperationId);
        Assert.Equal(ra.Id, cycle.Entries[1].ResourceId);
        Assert.Equal(opA.Id, cycle.Entries[1].OwnerId);
    }

    [Fact]
    public void ThreeOperationOwnershipCycleIsReportedAsDeadlock()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ra = scheduler.CreateResource(ControlledResourceKind.Monitor, "A");
        var rb = scheduler.CreateResource(ControlledResourceKind.Monitor, "B");
        var rc = scheduler.CreateResource(ControlledResourceKind.Monitor, "C");

        var opA = scheduler.Schedule("A", () => scheduler.WaitOnResource(rb, Reason("B")));
        var opB = scheduler.Schedule("B", () => scheduler.WaitOnResource(rc, Reason("C")));
        var opC = scheduler.Schedule("C", () => scheduler.WaitOnResource(ra, Reason("A")));
        scheduler.MarkResourceOwner(ra, opA);
        scheduler.MarkResourceOwner(rb, opB);
        scheduler.MarkResourceOwner(rc, opC);

        scheduler.Drain();

        var report = scheduler.DetectDeadlock();
        Assert.Equal(ControlledLivenessState.Deadlocked, report.Liveness);
        var cycle = Assert.Single(report.Cycles);
        Assert.Equal([opA.Id, opB.Id, opC.Id], cycle.Entries.Select(e => e.OperationId));
        Assert.Contains("Deadlock cycle", scheduler.DescribeLiveness(), StringComparison.Ordinal);
    }

    [Fact]
    public void NonCyclicOwnershipChainIsNotDeadlocked()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var rb = scheduler.CreateResource(ControlledResourceKind.Monitor, "B");
        var rc = scheduler.CreateResource(ControlledResourceKind.Monitor, "C");

        // A waits on B's resource; B waits on an ownerless resource -> chain bottoms out, no cycle.
        var opA = scheduler.Schedule("A", () => scheduler.WaitOnResource(rb, Reason("B")));
        var opB = scheduler.Schedule("B", () => scheduler.WaitOnResource(rc, Reason("C")));
        scheduler.MarkResourceOwner(rb, opB);
        // rc intentionally left unowned.

        scheduler.Drain();

        var report = scheduler.DetectDeadlock();
        Assert.False(report.IsDeadlocked);
        Assert.Empty(report.Cycles);
        Assert.Equal(ControlledLivenessState.ExternallyCompletable, report.Liveness);
        Assert.Equal(2, report.BlockedCount);
    }

    [Fact]
    public void TimedWaitsInAnOwnershipCycleAreClassifiedPausedUntilTimeNotDeadlocked()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ra = scheduler.CreateResource(ControlledResourceKind.Monitor, "A");
        var rb = scheduler.CreateResource(ControlledResourceKind.Monitor, "B");

        var opA = scheduler.Schedule("A", () => scheduler.WaitOnResource(rb, TimeSpan.FromSeconds(5), Reason("B")));
        var opB = scheduler.Schedule("B", () => scheduler.WaitOnResource(ra, TimeSpan.FromSeconds(5), Reason("A")));
        scheduler.MarkResourceOwner(ra, opA);
        scheduler.MarkResourceOwner(rb, opB);

        // Park both without advancing time.
        Assert.True(scheduler.RunStep());
        Assert.True(scheduler.RunStep());
        Assert.False(scheduler.RunStep());

        var report = scheduler.DetectDeadlock();
        Assert.False(report.IsDeadlocked);
        Assert.Empty(report.Cycles);
        Assert.Equal(ControlledLivenessState.PausedUntilTime, report.Liveness);
        Assert.Equal(2, report.PendingTimeoutCount);
    }

    [Fact]
    public void ExternallyCompletableWaitIsNotDeadlocked()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.ManualResetEvent, "evt");
        scheduler.Schedule("op", () => scheduler.WaitOnResource(resource, Reason("evt")));

        Assert.True(scheduler.RunStep());
        Assert.False(scheduler.RunStep());

        var report = scheduler.DetectDeadlock();
        Assert.Equal(ControlledLivenessState.ExternallyCompletable, report.Liveness);
        Assert.False(report.IsDeadlocked);
        Assert.Equal(1, report.BlockedCount);
    }

    [Fact]
    public void ProgressingWhenAnotherOperationIsStillRunnable()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Monitor, "m");
        scheduler.Schedule("waiter", () => scheduler.WaitOnResource(resource, Reason("m")));
        scheduler.Schedule("worker", () => { });

        // Park the waiter; "worker" is still runnable.
        Assert.True(scheduler.RunStep());

        var report = scheduler.DetectDeadlock();
        Assert.Equal(ControlledLivenessState.Progressing, report.Liveness);
        Assert.Equal(1, report.RunnableCount);
    }

    [Fact]
    public void QuiescentWhenAllOperationsHaveCompleted()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        scheduler.Schedule("a", () => { });
        scheduler.Schedule("b", () => { });
        scheduler.Drain();

        var report = scheduler.DetectDeadlock();
        Assert.Equal(ControlledLivenessState.Quiescent, report.Liveness);
        Assert.Empty(report.Cycles);
        Assert.Equal(0, report.BlockedCount);
    }

    [Fact]
    public void SignaledWaitDoesNotStayDeadlocked()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var ra = scheduler.CreateResource(ControlledResourceKind.Monitor, "A");
        var rb = scheduler.CreateResource(ControlledResourceKind.Monitor, "B");

        var opA = scheduler.Schedule("A", () => scheduler.WaitOnResource(rb, Reason("B")));
        var opB = scheduler.Schedule("B", () => scheduler.WaitOnResource(ra, Reason("A")));
        scheduler.MarkResourceOwner(ra, opA);
        scheduler.MarkResourceOwner(rb, opB);

        scheduler.Drain();
        Assert.True(scheduler.DetectDeadlock().IsDeadlocked);

        // Breaking the cycle by signaling one waiter must clear the deadlock classification.
        Assert.NotNull(scheduler.SignalOne(rb));
        var afterSignal = scheduler.DetectDeadlock();
        Assert.NotEqual(ControlledLivenessState.Deadlocked, afterSignal.Liveness);
    }
}
