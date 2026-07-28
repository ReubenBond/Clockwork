namespace Clockwork.Tests;

public sealed class SimulationSchedulerTests
{
    [Fact]
    public void ScheduledTasksExecuteOnlyWhenStepped()
    {
        var (queue, _, scheduler) = CreateComponents();
        var executed = false;
        var task = new Task(() => executed = true);

        task.Start(scheduler);

        Assert.False(executed);
        Assert.True(queue.RunOnce());
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

        Assert.Equal(5, queue.RunUntilIdle());
        Assert.Equal([0, 1, 2, 3, 4], executionOrder);
    }

    [Fact]
    public void DelayedWorkWaitsForSimulatedTime()
    {
        var (queue, clock, _) = CreateComponents();
        var executed = false;

        queue.EnqueueAfter(() => executed = true, TimeSpan.FromSeconds(5));

        Assert.False(queue.RunOnce());
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(queue.RunOnce());
        Assert.True(executed);
    }

    [Fact]
    public void ItemsWithEarlierDueTimesRunBeforeLaterOnesRegardlessOfEnqueueOrder()
    {
        var (queue, clock, _) = CreateComponents();
        var executionOrder = new List<string>();

        // Enqueue the later-due item first to prove ordering is by due time, not enqueue order.
        queue.EnqueueAfter(() => executionOrder.Add("later"), TimeSpan.FromSeconds(10));
        queue.EnqueueAfter(() => executionOrder.Add("earlier"), TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(2, queue.RunUntilIdle());
        Assert.Equal(["earlier", "later"], executionOrder);
    }

    [Fact]
    public void ItemsWithTheSameDueTimeRunInSequenceNumberOrder()
    {
        var (queue, clock, _) = CreateComponents();
        var executionOrder = new List<int>();

        for (var i = 0; i < 5; i++)
        {
            var index = i;
            queue.EnqueueAfter(() => executionOrder.Add(index), TimeSpan.FromSeconds(5));
        }

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(5, queue.RunUntilIdle());
        Assert.Equal([0, 1, 2, 3, 4], executionOrder);
    }

    [Fact]
    public void DisposingAScheduledItemCancelsItBeforeItRuns()
    {
        var (queue, clock, _) = CreateComponents();
        var executed = false;

        var item = queue.EnqueueAfter(new ScheduledActionItem(() => executed = true), TimeSpan.FromSeconds(5));
        item.Dispose();

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.False(queue.RunOnce());
        Assert.False(executed);
    }

    [Fact]
    public void SynchronizationContextPostUsesTheQueue()
    {
        var (queue, _, _) = CreateComponents();
        var executed = false;

        queue.SynchronizationContext.Post(_ => executed = true, null);

        Assert.False(executed);
        Assert.True(queue.RunOnce());
        Assert.True(executed);
    }

    private static (SimulationSchedulerLane Queue, SimulationClock Clock, SimulationTaskScheduler Scheduler) CreateComponents()
    {
        var scheduler = SimulationTestHarness.NewScheduler();
        var clock = new SimulationClock(scheduler);
        var queue = new SimulationSchedulerLane(scheduler, SimulationTestHarness.NewGuard(scheduler));
        return (queue, clock, new SimulationTaskScheduler(queue));
    }
}
