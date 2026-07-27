namespace Clockwork.Tests;

/// <summary>
/// Covers <see cref="SimulationBarrier"/>: cyclic N-participant rendezvous semantics, deterministic
/// FIFO release order dispatched through the queue, barrier reset/reuse across rounds, and
/// cancellation retracting an arrival instead of silently counting toward release.
/// </summary>
public sealed class SimulationBarrierTests
{
    [Fact]
    public void ArrivedCountIncrementsPerArrivalAndNoOneIsReleasedBeforeAllParticipantsArrive()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 3);

        var first = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, barrier.ArrivedCount);
        Assert.False(first.IsCompleted);

        var second = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, barrier.ArrivedCount);
        Assert.False(second.IsCompleted);
        Assert.False(queue.HasItems);
    }

    [Fact]
    public void TheFinalArrivalReleasesEveryParticipantOfThatRoundInFifoOrderThroughTheQueue()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 3);

        var first = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        var second = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        var third = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, barrier.ArrivedCount); // Reset immediately upon release.
        Assert.False(first.IsCompleted); // Release is scheduled, not applied inline.

        Assert.True(queue.RunOnce());
        Assert.True(first.IsCompletedSuccessfully);
        Assert.False(second.IsCompleted);

        Assert.True(queue.RunOnce());
        Assert.True(second.IsCompletedSuccessfully);
        Assert.False(third.IsCompleted);

        Assert.True(queue.RunOnce());
        Assert.True(third.IsCompletedSuccessfully);
    }

    [Fact]
    public void TheBarrierResetsAndCanBeReusedForASecondRound()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 2);

        _ = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        _ = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, queue.RunUntilIdle());

        var thirdRoundFirst = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, barrier.ArrivedCount);
        Assert.False(thirdRoundFirst.IsCompleted);

        var thirdRoundSecond = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, queue.RunUntilIdle());
        Assert.True(thirdRoundFirst.IsCompletedSuccessfully);
        Assert.True(thirdRoundSecond.IsCompletedSuccessfully);
    }

    [Fact]
    public void CancellingAWaitingParticipantRetractsItsArrivalSoItDoesNotCountTowardRelease()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 3);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var first = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        var canceled = barrier.ArriveAndWaitAsync(cts.Token);
        Assert.Equal(2, barrier.ArrivedCount);

        cts.Cancel();
        Assert.True(canceled.IsCanceled);
        Assert.Equal(1, barrier.ArrivedCount); // Retracted - back down to just "first".

        // Two more arrivals are now needed (not one) to complete the round.
        var third = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, barrier.ArrivedCount);
        Assert.False(first.IsCompleted);

        var fourth = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, barrier.ArrivedCount);

        Assert.Equal(3, queue.RunUntilIdle());
        Assert.True(first.IsCompletedSuccessfully);
        Assert.True(third.IsCompletedSuccessfully);
        Assert.True(fourth.IsCompletedSuccessfully);
    }

    [Fact]
    public void ArriveAndWaitAsyncWithAnAlreadyCanceledTokenReturnsACanceledTaskWithoutArriving()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 2);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        var task = barrier.ArriveAndWaitAsync(cts.Token);

        Assert.True(task.IsCanceled);
        Assert.Equal(0, barrier.ArrivedCount);
    }

    [Fact]
    public void ConstructorRejectsANonPositiveParticipantCount()
    {
        var queue = CreateQueue();
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationBarrier(queue, participantCount: 0));
    }

    [Fact]
    public void ASingleParticipantBarrierReleasesImmediatelyOnEachArrival()
    {
        var queue = CreateQueue();
        var barrier = new SimulationBarrier(queue, participantCount: 1);

        var task = barrier.ArriveAndWaitAsync(TestContext.Current.CancellationToken);

        Assert.True(queue.RunOnce());
        Assert.True(task.IsCompletedSuccessfully);
    }

    private static SimulationTaskQueue CreateQueue() => new(new SimulationClock(DateTimeOffset.UnixEpoch), new SingleThreadedGuard());
}
