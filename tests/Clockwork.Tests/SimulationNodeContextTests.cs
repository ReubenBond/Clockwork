namespace Clockwork.Tests;

public sealed class SimulationNodeContextTests
{
    [Fact]
    public void NewContextStartsInRunningState()
    {
        var context = CreateContext();

        Assert.Equal(SimulationNodeState.Running, context.State);
        Assert.False(context.IsSuspended);
    }

    [Fact]
    public void SuspendPreventsStepFromRunningReadyWork()
    {
        var context = CreateContext();
        var executed = false;
        context.TaskQueue.Enqueue(new ScheduledActionItem(() => executed = true));

        context.Suspend();

        Assert.True(context.IsSuspended);
        Assert.False(context.Step());
        Assert.False(executed);
    }

    [Fact]
    public void ResumeAllowsPreviouslyQueuedReadyWorkToRun()
    {
        var context = CreateContext();
        var executed = false;
        context.TaskQueue.Enqueue(new ScheduledActionItem(() => executed = true));
        context.Suspend();

        context.Resume();

        Assert.False(context.IsSuspended);
        Assert.True(context.Step());
        Assert.True(executed);
    }

    [Fact]
    public void RunUntilIdleReturnsZeroWhileSuspendedEvenWithReadyWork()
    {
        var context = CreateContext();
        context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        context.TaskQueue.Enqueue(new ScheduledActionItem(() => { }));
        context.Suspend();

        Assert.Equal(0, context.RunUntilIdle());
    }

    [Fact]
    public void HasReadyTasksReflectsQueueStateAndSuspension()
    {
        var (clock, guard, context) = CreateComponents();

        Assert.False(context.HasReadyTasks);

        context.TaskQueue.EnqueueAfter(() => { }, TimeSpan.FromSeconds(5));
        Assert.False(context.HasReadyTasks);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(context.HasReadyTasks);

        context.Suspend();
        Assert.False(context.HasReadyTasks);
    }

    [Fact]
    public void SuspendForWithoutAnExternalQueueThrows()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var guard = new SingleThreadedGuard();
        var context = new SimulationNodeContext(clock, guard, new SimulationRandom(1));

        Assert.Throws<InvalidOperationException>(() => context.SuspendFor(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SuspendForAutomaticallyResumesAfterTheDurationElapses()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var guard = new SingleThreadedGuard();
        var externalQueue = new SimulationTaskQueue(clock, guard);
        var context = new SimulationNodeContext(clock, guard, new SimulationRandom(1), externalQueue);

        context.SuspendFor(TimeSpan.FromSeconds(5));
        Assert.True(context.IsSuspended);

        // The auto-resume is scheduled on the external queue, not the node's own queue.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(context.IsSuspended);

        Assert.True(externalQueue.RunOnce());
        Assert.False(context.IsSuspended);
    }

    [Fact]
    public void SuspendForWithNonPositiveDurationThrows()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var guard = new SingleThreadedGuard();
        var externalQueue = new SimulationTaskQueue(clock, guard);
        var context = new SimulationNodeContext(clock, guard, new SimulationRandom(1), externalQueue);

        Assert.Throws<ArgumentOutOfRangeException>(() => context.SuspendFor(TimeSpan.Zero));
    }

    private static (SimulationClock Clock, SingleThreadedGuard Guard, SimulationNodeContext Context) CreateComponents()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var guard = new SingleThreadedGuard();
        var context = new SimulationNodeContext(clock, guard, new SimulationRandom(1));
        return (clock, guard, context);
    }

    private static SimulationNodeContext CreateContext() => CreateComponents().Context;
}
