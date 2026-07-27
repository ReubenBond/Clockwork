namespace Clockwork.Tests;

public sealed class SimulationClockTests
{
    [Fact]
    public void UtcNowStartsAtTheConfiguredStartDateTime()
    {
        var start = DateTimeOffset.UnixEpoch + TimeSpan.FromDays(1);
        var clock = new SimulationClock(start);

        Assert.Equal(start, clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.CurrentTime);
    }

    [Fact]
    public void AdvanceMovesUtcNowForwardByTheDelta()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);

        clock.Advance(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(7), clock.CurrentTime);
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(7), clock.UtcNow);
    }

    [Fact]
    public void AdvanceWithNegativeDeltaThrows()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AdvanceWithZeroDeltaDoesNotChangeCurrentTime()
    {
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(3));

        clock.Advance(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(3), clock.CurrentTime);
    }

    [Fact]
    public void MultipleQueuesSharingAClockObserveTheSameAdvancesAtomically()
    {
        // The clock is the single source of truth shared across a node's queue and the
        // cluster-level queue - advancing it must unblock ready work on every queue at once.
        var clock = new SimulationClock(DateTimeOffset.UnixEpoch);
        var guard = new SingleThreadedGuard();
        var queueA = new SimulationTaskQueue(clock, guard);
        var queueB = new SimulationTaskQueue(clock, guard);

        var aExecuted = false;
        var bExecuted = false;
        queueA.EnqueueAfter(() => aExecuted = true, TimeSpan.FromSeconds(5));
        queueB.EnqueueAfter(() => bExecuted = true, TimeSpan.FromSeconds(5));

        Assert.False(queueA.RunOnce());
        Assert.False(queueB.RunOnce());

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.True(queueA.RunOnce());
        Assert.True(queueB.RunOnce());
        Assert.True(aExecuted);
        Assert.True(bExecuted);
    }
}
