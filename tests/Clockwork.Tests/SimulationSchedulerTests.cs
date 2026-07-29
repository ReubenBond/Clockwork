using Clockwork.Runtime.Scheduling;

namespace Clockwork.Tests;

public sealed class SimulationSchedulerTests
{
    [Fact]
    public void SchedulerTimeStartsAtConfiguredOrigin()
    {
        var start = DateTimeOffset.UnixEpoch + TimeSpan.FromDays(1);
        using var scheduler = SimulationTestHarness.NewScheduler(start);

        Assert.Equal(start, scheduler.StartDateTime);
        Assert.Equal(start, scheduler.UtcNow);
        Assert.Equal(TimeSpan.Zero, scheduler.VirtualTime);
    }

    [Fact]
    public void AdvancingVirtualTimeMovesSchedulerTimeForward()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();

        scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(5));
        scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(7));

        Assert.Equal(TimeSpan.FromSeconds(7), scheduler.VirtualTime);
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(7), scheduler.UtcNow);
    }

    [Fact]
    public void AdvancingVirtualTimeRejectsInvalidTargetsAndAllowsCurrentTime()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(-1)));

        scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(3));
        scheduler.AdvanceVirtualTimeTo(scheduler.VirtualTime);

        Assert.Equal(TimeSpan.FromSeconds(3), scheduler.VirtualTime);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void LanesSharingSchedulerObserveVirtualTimeAdvanceTogether()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();
        var guard = SimulationTestHarness.NewGuard(scheduler);
        var queueA = new SimulationSchedulerLane(scheduler, guard);
        var queueB = new SimulationSchedulerLane(scheduler, guard);
        var aExecuted = false;
        var bExecuted = false;
        queueA.EnqueueAfter(() => aExecuted = true, TimeSpan.FromSeconds(5));
        queueB.EnqueueAfter(() => bExecuted = true, TimeSpan.FromSeconds(5));

        Assert.False(queueA.RunOnce(TestContext.Current.CancellationToken));
        Assert.False(queueB.RunOnce(TestContext.Current.CancellationToken));

        scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(5));

        Assert.True(queueA.RunOnce(TestContext.Current.CancellationToken));
        Assert.True(queueB.RunOnce(TestContext.Current.CancellationToken));
        Assert.True(aExecuted);
        Assert.True(bExecuted);
    }

    [Fact]
    public void ScheduledTasksExecuteOnlyWhenStepped()
    {
        var (queue, _, scheduler) = CreateComponents();
        var executed = false;
        var task = new Task(() => executed = true);

        task.Start(scheduler);

        Assert.False(executed);
        Assert.True(queue.RunOnce(TestContext.Current.CancellationToken));
        Assert.True(executed);
    }

    [Fact]
    public void TasksExecuteInFifoOrder()
    {
        var (queue, _, scheduler) = CreateComponents();
        var executionOrder = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var index = i;
            var task = new Task(() => executionOrder.Add(index));
            task.Start(scheduler);
        }

        Assert.Equal(5, queue.RunUntilIdle(TestContext.Current.CancellationToken));
        Assert.Equal([0, 1, 2, 3, 4], executionOrder);
    }

    [Fact]
    public void RunUntilIdleCanCancelSelfPerpetuatingLaneWork()
    {
        var (queue, _, _) = CreateComponents();
        using var cancellation = new CancellationTokenSource();
        var executions = 0;
        Action? runAgain = null;
        runAgain = () =>
        {
            if (++executions == 3)
            {
                cancellation.Cancel();
                queue.Enqueue(() => { });
                return;
            }

            queue.Enqueue(runAgain!);
        };
        queue.Enqueue(runAgain);

        var exception = Assert.Throws<OperationCanceledException>(
            () => queue.RunUntilIdle(cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(3, executions);
    }

    [Fact]
    public void EmptyLanePumpHonorsPreCanceledToken()
    {
        var (queue, _, _) = CreateComponents();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => queue.RunUntilIdle(cancellation.Token));
    }

    [Fact]
    public void DelayedWorkWaitsForSimulatedTime()
    {
        var (queue, scheduler, _) = CreateComponents();
        var executed = false;

        queue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        Assert.False(queue.RunOnce(TestContext.Current.CancellationToken));
        Advance(scheduler, TimeSpan.FromSeconds(5));
        Assert.True(queue.RunOnce(TestContext.Current.CancellationToken));
        Assert.True(executed);
    }

    [Fact]
    public void ItemsWithEarlierDueTimesRunBeforeLaterOnesRegardlessOfEnqueueOrder()
    {
        var (queue, scheduler, _) = CreateComponents();
        var executionOrder = new List<string>();

        // Enqueue the later-due item first to prove ordering is by due time, not enqueue order.
        queue.EnqueueAfter(() => executionOrder.Add("later"), TimeSpan.FromSeconds(10));
        queue.EnqueueAfter(() => executionOrder.Add("earlier"), TimeSpan.FromSeconds(5));

        Advance(scheduler, TimeSpan.FromSeconds(10));

        Assert.Equal(2, queue.RunUntilIdle(TestContext.Current.CancellationToken));
        Assert.Equal(["earlier", "later"], executionOrder);
    }

    [Fact]
    public void ItemsWithTheSameDueTimeRunInSequenceNumberOrder()
    {
        var (queue, scheduler, _) = CreateComponents();
        var executionOrder = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var index = i;
            queue.EnqueueAfter(() => executionOrder.Add(index), TimeSpan.FromSeconds(5));
        }

        Advance(scheduler, TimeSpan.FromSeconds(5));

        Assert.Equal(5, queue.RunUntilIdle(TestContext.Current.CancellationToken));
        Assert.Equal([0, 1, 2, 3, 4], executionOrder);
    }

    [Fact]
    public void ScheduledItemDiagnosticsAreImmutableSnapshots()
    {
        var (queue, scheduler, _) = CreateComponents();

        queue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(5));
        IReadOnlyList<SimulationScheduledItemDiagnostic> snapshot = queue.CaptureScheduledItems();

        SimulationScheduledItemDiagnostic item = Assert.Single(snapshot);
        Assert.Equal("action", item.Kind);
        Assert.Equal("Scheduled action", item.Description);
        Assert.Equal(scheduler.UtcNow + TimeSpan.FromSeconds(5), item.DueTime);
        Assert.Equal(0, item.SequenceNumber);
        Assert.False(item.IsReady);
        Assert.False(item.IsBlocked);

        queue.Enqueue(() => { });
        Assert.Single(snapshot);
        Assert.Equal(2, queue.CaptureScheduledItems().Count);
    }

    [Fact]
    public void SynchronizationContextPostUsesTheQueue()
    {
        var (queue, _, _) = CreateComponents();
        var executed = false;

        queue.SynchronizationContext.Post(_ => executed = true, null);

        Assert.False(executed);
        Assert.True(queue.RunOnce(TestContext.Current.CancellationToken));
        Assert.True(executed);
    }

    private static (SimulationSchedulerLane Queue, SimulationScheduler RuntimeScheduler, SimulationTaskScheduler TaskScheduler) CreateComponents()
    {
        var scheduler = SimulationTestHarness.NewScheduler();
        var queue = new SimulationSchedulerLane(scheduler, SimulationTestHarness.NewGuard(scheduler));
        return (queue, scheduler, new SimulationTaskScheduler(queue));
    }

    private static void Advance(SimulationScheduler scheduler, TimeSpan delta) =>
        scheduler.AdvanceVirtualTimeTo(scheduler.VirtualTime + delta);
}
