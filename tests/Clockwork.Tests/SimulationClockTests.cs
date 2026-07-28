namespace Clockwork.Tests;

public sealed class SimulationClockTests
{
    [Fact]
    public void UtcNowStartsAtTheConfiguredStartDateTime()
    {
        var start = DateTimeOffset.UnixEpoch + TimeSpan.FromDays(1);
        using var scheduler = SimulationTestHarness.NewScheduler(start);
        var clock = new SimulationClock(scheduler);

        Assert.Equal(start, clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.CurrentTime);
    }

    [Fact]
    public void AdvanceMovesUtcNowForwardByTheDelta()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();
        var clock = new SimulationClock(scheduler);

        clock.Advance(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(7), clock.CurrentTime);
        Assert.Equal(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(7), clock.UtcNow);
    }

    [Fact]
    public void AdvanceWithNegativeDeltaThrows()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();
        var clock = new SimulationClock(scheduler);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AdvanceWithZeroDeltaDoesNotChangeCurrentTime()
    {
        using var scheduler = SimulationTestHarness.NewScheduler();
        scheduler.AdvanceVirtualTimeTo(TimeSpan.FromSeconds(3));
        var clock = new SimulationClock(scheduler);

        clock.Advance(TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(3), clock.CurrentTime);
    }

    [Fact]
    public void MultipleQueuesSharingAClockObserveTheSameAdvancesAtomically()
    {
        // The clock is the single source of truth shared across a node's queue and the
        // cluster-level queue - advancing it must unblock ready work on every queue at once.
        using var scheduler = SimulationTestHarness.NewScheduler();
        var clock = new SimulationClock(scheduler);
        var guard = SimulationTestHarness.NewGuard(scheduler);
        var queueA = new SimulationSchedulerLane(scheduler, guard);
        var queueB = new SimulationSchedulerLane(scheduler, guard);

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
