namespace Clockwork.Tests;

/// <summary>
/// Covers <see cref="SimulationGate"/>: level-triggered open/close semantics, deterministic FIFO
/// release order dispatched through the queue (never inline), reusability across multiple
/// open/close cycles, and cancellation (both already-canceled and racing with release).
/// </summary>
public sealed class SimulationGateTests
{
    [Fact]
    public void WaitAsyncOnAnAlreadyOpenGateCompletesSynchronouslyWithoutTouchingTheQueue()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue, isOpen: true);

        var task = gate.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.False(queue.HasItems);
    }

    [Fact]
    public void WaitAsyncOnAClosedGateDoesNotCompleteUntilOpenIsCalledAndTheQueueIsDrained()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);

        var task = gate.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        gate.Open();
        Assert.False(task.IsCompleted); // Release is scheduled, not applied inline.

        Assert.True(queue.RunOnce());
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void OpenReleasesMultipleWaitersInFifoOrderThroughTheQueue()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);

        var first = gate.WaitAsync(TestContext.Current.CancellationToken);
        var second = gate.WaitAsync(TestContext.Current.CancellationToken);
        var third = gate.WaitAsync(TestContext.Current.CancellationToken);

        gate.Open();

        Assert.True(queue.RunOnce());
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);
        Assert.False(third.IsCompleted);

        Assert.True(queue.RunOnce());
        Assert.True(second.IsCompletedSuccessfully);
        Assert.False(third.IsCompleted);

        Assert.True(queue.RunOnce());
        Assert.True(third.IsCompletedSuccessfully);
    }

    [Fact]
    public void CloseThenOpenAgainReleasesANewRoundOfWaiters()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);

        gate.Open(); // No waiters registered yet - nothing to release.
        Assert.Equal(0, queue.RunUntilIdle());

        gate.Close();
        Assert.False(gate.IsOpen);

        var task = gate.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        gate.Open();
        Assert.Equal(1, queue.RunUntilIdle());
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void OpeningAnAlreadyOpenGateIsIdempotentAndDoesNotThrow()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue, isOpen: true);

        gate.Open();
        gate.Open();

        Assert.True(gate.IsOpen);
    }

    [Fact]
    public void WaitAsyncWithAnAlreadyCanceledTokenReturnsACanceledTaskWithoutRegisteringAWaiter()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        var task = gate.WaitAsync(cts.Token);

        Assert.True(task.IsCanceled);

        gate.Open();
        Assert.False(queue.HasItems); // The canceled call never registered a waiter to release.
    }

    [Fact]
    public void CancellingBeforeTheQueueDrainsTheReleaseWinsTheRaceAndTheWaiterEndsUpCanceled()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var task = gate.WaitAsync(cts.Token);

        gate.Open(); // Enqueues the completion, but does not run it yet.
        cts.Cancel(); // Cancellation is synchronous and happens before the queue is drained.

        Assert.True(task.IsCanceled);

        // The still-queued completion action must be a harmless no-op once drained.
        Assert.True(queue.RunOnce());
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public void CancellingAfterReleaseHasAlreadyRunIsANoOp()
    {
        var queue = CreateQueue();
        var gate = new SimulationGate(queue);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var task = gate.WaitAsync(cts.Token);
        gate.Open();
        Assert.True(queue.RunOnce());
        Assert.True(task.IsCompletedSuccessfully);

        cts.Cancel();

        Assert.True(task.IsCompletedSuccessfully); // Still successfully completed, not canceled.
    }

    [Fact]
    public void NameIsExposedForDiagnosticsAndDefaultsToNull()
    {
        var queue = CreateQueue();
        Assert.Null(new SimulationGate(queue).Name);
        Assert.Equal("startup", new SimulationGate(queue, name: "startup").Name);
    }

    private static SimulationTaskQueue CreateQueue() => new(new SimulationClock(DateTimeOffset.UnixEpoch), new SingleThreadedGuard());
}
