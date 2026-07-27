using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Unit tests for the self-contained <see cref="ControlledTaskLoop"/>: FIFO ready ordering, readiness
/// promotion, deterministic single-step pumping, idle detection, and deadlock signalling. These need no
/// active simulation - the loop is a pure deterministic data structure.
/// </summary>
public sealed class ControlledTaskLoopTests
{
    [Fact]
    public void ScheduleRunsContinuationsInFifoOrder()
    {
        var loop = new ControlledTaskLoop();
        var order = new List<int>();

        loop.Schedule(() => order.Add(1));
        loop.Schedule(() => order.Add(2));
        loop.Schedule(() => order.Add(3));

        var executed = loop.RunUntilIdle();

        Assert.Equal(3, executed);
        Assert.Equal([1, 2, 3], order);
        Assert.True(loop.IsIdle);
    }

    [Fact]
    public void RunUntilIdleRunsWorkScheduledByRunningContinuations()
    {
        var loop = new ControlledTaskLoop();
        var order = new List<int>();

        loop.Schedule(() =>
        {
            order.Add(1);
            loop.Schedule(() => order.Add(2));
        });

        loop.RunUntilIdle();

        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void ScheduleWhenReadyDefersUntilPredicateHolds()
    {
        var loop = new ControlledTaskLoop();
        var ran = false;
        var ready = false;

        loop.ScheduleWhenReady(() => ready, () => ran = true);

        // Nothing ready: the loop reaches idle without running the gated continuation.
        Assert.Equal(0, loop.RunUntilIdle());
        Assert.False(ran);
        Assert.Equal(1, loop.WaitingCount);

        ready = true;
        Assert.Equal(1, loop.RunUntilIdle());
        Assert.True(ran);
        Assert.True(loop.IsIdle);
    }

    [Fact]
    public void PromotionPreservesInsertionOrderAmongReadyWaits()
    {
        var loop = new ControlledTaskLoop();
        var order = new List<int>();

        loop.ScheduleWhenReady(() => true, () => order.Add(1));
        loop.ScheduleWhenReady(() => true, () => order.Add(2));
        loop.ScheduleWhenReady(() => true, () => order.Add(3));

        loop.RunUntilIdle();

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void ReadyWorkRunsBeforePromotedWaits()
    {
        var loop = new ControlledTaskLoop();
        var order = new List<int>();

        loop.ScheduleWhenReady(() => true, () => order.Add(2));
        loop.Schedule(() => order.Add(1));

        loop.RunUntilIdle();

        // The already-ready item (scheduled second) runs before the promoted wait (registered first),
        // because promotion appends to the tail of the FIFO ready queue.
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public void RunUntilCompletesWhenPredicateBecomesTrue()
    {
        var loop = new ControlledTaskLoop();
        var done = false;

        loop.Schedule(() => done = true);
        loop.RunUntil(() => done, "test");

        Assert.True(done);
    }

    [Fact]
    public void RunUntilReturnsImmediatelyWhenAlreadyComplete()
    {
        var loop = new ControlledTaskLoop();
        var ran = false;
        loop.Schedule(() => ran = true);

        loop.RunUntil(() => true, "test");

        // Already complete before pumping: no continuation runs.
        Assert.False(ran);
    }

    [Fact]
    public void RunUntilThrowsDeadlockWhenNoProgressPossible()
    {
        var loop = new ControlledTaskLoop();
        loop.ScheduleWhenReady(() => false, () => { });

        var ex = Assert.Throws<ControlledSynchronousWaitDeadlockException>(
            () => loop.RunUntil(() => false, "test.wait"));
        Assert.Equal("test.wait", ex.ApiName);
    }

    [Fact]
    public void RunUntilThrowsDeadlockWhenEmptyAndIncomplete()
    {
        var loop = new ControlledTaskLoop();

        Assert.Throws<ControlledSynchronousWaitDeadlockException>(
            () => loop.RunUntil(() => false, "test.wait"));
    }

    [Fact]
    public void ChainedReadinessResolvesAcrossPumps()
    {
        var loop = new ControlledTaskLoop();
        var stage = 0;

        // Continuation A completes stage 1, which makes B ready, which completes stage 2.
        loop.ScheduleWhenReady(() => stage >= 1, () => stage = 2);
        loop.Schedule(() => stage = 1);

        loop.RunUntil(() => stage == 2, "test");

        Assert.Equal(2, stage);
    }

    [Fact]
    public void CountsReflectPendingWork()
    {
        var loop = new ControlledTaskLoop();
        Assert.True(loop.IsIdle);

        loop.Schedule(() => { });
        loop.ScheduleWhenReady(() => false, () => { });

        Assert.Equal(1, loop.ReadyCount);
        Assert.Equal(1, loop.WaitingCount);
        Assert.False(loop.IsIdle);
    }

    [Fact]
    public void ScheduleRejectsNullContinuation()
    {
        var loop = new ControlledTaskLoop();
        Assert.Throws<ArgumentNullException>(() => loop.Schedule(null!));
        Assert.Throws<ArgumentNullException>(() => loop.ScheduleWhenReady(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => loop.ScheduleWhenReady(() => true, null!));
    }
}
