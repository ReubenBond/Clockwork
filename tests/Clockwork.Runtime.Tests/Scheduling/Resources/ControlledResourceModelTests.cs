using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Runtime.Tests.Scheduling.Resources;

/// <summary>
/// Coverage of the reusable resource model itself (identity, metadata, and the deterministic FIFO
/// wait queue), independent of the wait/wake scheduler integration exercised elsewhere.
/// </summary>
public sealed class ControlledResourceModelTests
{
    [Fact]
    public void CreateResourceAssignsStableIncreasingIdsAndRegistersThem()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();

        var a = scheduler.CreateResource(ControlledResourceKind.Monitor, "lock-a");
        var b = scheduler.CreateResource(ControlledResourceKind.Semaphore, "sem-b");

        Assert.False(a.Id.IsNone);
        Assert.True(a.Id < b.Id);
        Assert.Equal(ControlledResourceKind.Monitor, a.Kind);
        Assert.Equal("lock-a", a.Name);

        var all = scheduler.CaptureResources();
        Assert.Equal([a.Id, b.Id], all.Select(r => r.Id));
    }

    [Fact]
    public void NewResourceStartsUnownedWithNoWaitersAndDefaultCounts()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var r = scheduler.CreateResource(ControlledResourceKind.Custom, "r");

        Assert.Null(r.Owner);
        Assert.Equal(0, r.RecursionCount);
        Assert.Equal(0, r.CurrentCount);
        Assert.Equal(int.MaxValue, r.MaximumCount);
        Assert.False(r.IsSignaled);
        Assert.Equal(0, r.WaiterCount);
        Assert.False(r.HasPendingWaiters);
        Assert.Empty(r.SnapshotWaiters());
    }

    [Fact]
    public void ResourceIdNoneComparesAndFormatsDeterministically()
    {
        Assert.True(ControlledResourceId.None.IsNone);
        Assert.Equal("res:none", ControlledResourceId.None.ToString());
        Assert.Equal("res:5", new ControlledResourceId(5).ToString());
        Assert.True(new ControlledResourceId(1) < new ControlledResourceId(2));
        Assert.True(new ControlledResourceId(2) >= new ControlledResourceId(2));
    }

    [Fact]
    public void WaitQueueIsFifoByEnqueueSequenceAndPeeksEarliestPending()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Monitor, "m");
        var op1 = scheduler.Register("one", () => { });
        var op2 = scheduler.Register("two", () => { });
        var op3 = scheduler.Register("three", () => { });

        var w1 = new ControlledResourceWaiter(op1, resource, 10, ControlledOperationPauseReason.ResourceWait("m"));
        var w2 = new ControlledResourceWaiter(op2, resource, 11, ControlledOperationPauseReason.ResourceWait("m"));
        var w3 = new ControlledResourceWaiter(op3, resource, 12, ControlledOperationPauseReason.ResourceWait("m"));
        resource.EnqueueWaiter(w1);
        resource.EnqueueWaiter(w2);
        resource.EnqueueWaiter(w3);

        Assert.Equal(3, resource.WaiterCount);
        Assert.True(resource.HasPendingWaiters);
        Assert.Same(w1, resource.PeekNextPending());

        // Resolving the head advances the peek to the next unresolved waiter, in order.
        Assert.True(w1.TryResolve(ControlledWaitOutcome.Signaled));
        Assert.Same(w2, resource.PeekNextPending());

        // Removing a middle waiter keeps the remaining order intact.
        resource.RemoveWaiter(w2);
        Assert.Same(w3, resource.PeekNextPending());
        Assert.Equal([12L], resource.SnapshotPendingWaiters().Select(w => w.EnqueueSequence));
    }

    [Fact]
    public void WaiterResolvesExactlyOnce()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Custom, "r");
        var op = scheduler.Register("op", () => { });
        var waiter = new ControlledResourceWaiter(op, resource, 1, ControlledOperationPauseReason.ResourceWait("r"));

        Assert.False(waiter.IsResolved);
        Assert.True(waiter.TryResolve(ControlledWaitOutcome.Signaled));
        Assert.True(waiter.IsResolved);
        Assert.Equal(ControlledWaitOutcome.Signaled, waiter.Resolution);

        // A second resolution attempt (e.g. a timeout racing a signal) is rejected and does not
        // overwrite the first, decided outcome.
        Assert.False(waiter.TryResolve(ControlledWaitOutcome.TimedOut));
        Assert.Equal(ControlledWaitOutcome.Signaled, waiter.Resolution);
    }

    [Fact]
    public void SnapshotWaitersProducesDeterministicInfoInQueueOrder()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        var resource = scheduler.CreateResource(ControlledResourceKind.Semaphore, "sem");
        var op1 = scheduler.Register("a", () => { });
        var op2 = scheduler.Register("b", () => { });
        resource.EnqueueWaiter(new ControlledResourceWaiter(op1, resource, 1, ControlledOperationPauseReason.ResourceWait("sem")));
        resource.EnqueueWaiter(new ControlledResourceWaiter(op2, resource, 2, ControlledOperationPauseReason.ResourceWait("sem")));

        var info = resource.SnapshotWaiters();

        Assert.Equal(2, info.Count);
        Assert.Equal(op1.Id, info[0].OperationId);
        Assert.Equal("sem", info[0].ResourceName);
        Assert.Equal(1, info[0].EnqueueSequence);
        Assert.Null(info[0].Resolution);
        Assert.Equal(op2.Id, info[1].OperationId);
    }

    [Fact]
    public void CreateResourceRejectsEmptyName()
    {
        using var scheduler = SchedulerTestHarness.NewScheduler();
        Assert.Throws<ArgumentException>(() => scheduler.CreateResource(ControlledResourceKind.Custom, ""));
    }
}
