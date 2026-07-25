namespace Clockwork.Tests;

public sealed class SimulationTimeProviderTests
{
    [Fact]
    public void TimerFiresWhenSimulatedTimeReachesDueTime()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        using var timer = provider.CreateTimer(_ => fireCount++, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        Assert.False(queue.RunOnce());
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.True(queue.RunOnce());
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void PeriodicTimerReschedulesItself()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        using var timer = provider.CreateTimer(_ => fireCount++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(queue.RunOnce());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(queue.RunOnce());

        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void DisposedTimerDoesNotFire()
    {
        var (clock, queue, provider) = CreateComponents();
        var fired = false;
        var timer = provider.CreateTimer(_ => fired = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        timer.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.False(queue.RunOnce());
        Assert.False(fired);
    }

    private static (SimulationClock Clock, SimulationTaskQueue Queue, SimulationTimeProvider Provider) CreateComponents()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var queue = new SimulationTaskQueue(clock, new SingleThreadedGuard());
        return (clock, queue, new SimulationTimeProvider(queue, clock));
    }
}
