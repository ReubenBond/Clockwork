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

    [Fact]
    public void CallbackChangeToPeriodicReplacesAutomaticReschedule()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        SimulationTimer? timer = null;
        timer = (SimulationTimer)provider.CreateTimer(
            _ =>
            {
                fireCount++;
                if (fireCount == 1)
                {
                    _ = timer!.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(10));
        using (timer)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(queue.RunOnce());
            AssertSinglePendingTimer(queue, clock.UtcNow + TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True(queue.RunOnce());
            Assert.Equal(2, fireCount);
            AssertSinglePendingTimer(queue, clock.UtcNow + TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.True(queue.RunOnce());
            Assert.Equal(3, fireCount);
            AssertSinglePendingTimer(queue, clock.UtcNow + TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public void CallbackChangeToOneShotLeavesOnlyTheReplacementPending()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        SimulationTimer? timer = null;
        timer = (SimulationTimer)provider.CreateTimer(
            _ =>
            {
                fireCount++;
                if (fireCount == 1)
                {
                    _ = timer!.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        using (timer)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(queue.RunOnce());
            AssertSinglePendingTimer(queue, clock.UtcNow + TimeSpan.FromSeconds(3), TimeSpan.Zero);

            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True(queue.RunOnce());
            Assert.Equal(2, fireCount);
            Assert.Equal(0, SimulationTimer.GetPendingTimerCount(queue));
            Assert.False(queue.RunOnce());
        }
    }

    [Fact]
    public void CallbackChangeToInfiniteSuppressesAutomaticReschedule()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        SimulationTimer? timer = null;
        timer = (SimulationTimer)provider.CreateTimer(
            _ =>
            {
                fireCount++;
                _ = timer!.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        using (timer)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(queue.RunOnce());

            Assert.Equal(1, fireCount);
            Assert.Equal(0, SimulationTimer.GetPendingTimerCount(queue));
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.False(queue.RunOnce());
        }
    }

    [Fact]
    public void RepeatedCallbackChangesLeaveOnlyTheLastReplacementPending()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        SimulationTimer? timer = null;
        timer = (SimulationTimer)provider.CreateTimer(
            _ =>
            {
                fireCount++;
                if (fireCount == 1)
                {
                    _ = timer!.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
                    _ = timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6));
                    _ = timer.Change(TimeSpan.FromSeconds(4), Timeout.InfiniteTimeSpan);
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        using (timer)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(queue.RunOnce());
            AssertSinglePendingTimer(queue, clock.UtcNow + TimeSpan.FromSeconds(4), TimeSpan.Zero);

            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.True(queue.RunOnce());
            Assert.Equal(2, fireCount);
            Assert.Equal(0, SimulationTimer.GetPendingTimerCount(queue));
            Assert.False(queue.RunOnce());
        }
    }

    [Fact]
    public void CallbackDisposeSuppressesAutomaticReschedule()
    {
        var (clock, queue, provider) = CreateComponents();
        var fireCount = 0;
        SimulationTimer? timer = null;
        timer = (SimulationTimer)provider.CreateTimer(
            _ =>
            {
                fireCount++;
                timer!.Dispose();
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(queue.RunOnce());

        Assert.Equal(1, fireCount);
        Assert.Equal(0, SimulationTimer.GetPendingTimerCount(queue));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(queue.RunOnce());
    }

    private static void AssertSinglePendingTimer(
        SimulationTaskQueue queue,
        DateTimeOffset expectedDueTime,
        TimeSpan expectedPeriod)
    {
        Assert.Equal(1, SimulationTimer.GetPendingTimerCount(queue));
        var pending = Assert.Single(SimulationTimer.GetTimers(queue));
        Assert.Equal(expectedDueTime, pending.DueTime);
        Assert.Equal(expectedPeriod, pending.Period);
    }

    private static (SimulationClock Clock, SimulationTaskQueue Queue, SimulationTimeProvider Provider) CreateComponents()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var queue = new SimulationTaskQueue(clock, new SingleThreadedGuard());
        return (clock, queue, new SimulationTimeProvider(queue, clock));
    }
}
