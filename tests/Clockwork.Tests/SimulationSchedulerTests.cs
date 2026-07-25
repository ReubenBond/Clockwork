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
    public void SynchronizationContextPostUsesTheQueue()
    {
        var (queue, _, _) = CreateComponents();
        var executed = false;

        queue.SynchronizationContext.Post(_ => executed = true, null);

        Assert.False(executed);
        Assert.True(queue.RunOnce());
        Assert.True(executed);
    }

    private static (SimulationTaskQueue Queue, SimulationClock Clock, SimulationTaskScheduler Scheduler) CreateComponents()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var queue = new SimulationTaskQueue(clock, new SingleThreadedGuard());
        return (queue, clock, new SimulationTaskScheduler(queue));
    }
}
