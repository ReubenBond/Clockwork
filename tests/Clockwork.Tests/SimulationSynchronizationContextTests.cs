namespace Clockwork.Tests;

public sealed class SimulationSynchronizationContextTests
{
    [Fact]
    public void InstallSetsCurrentToTheSimulationContextAndDisposeRestoresThePrevious()
    {
        var queue = CreateQueue();
        var previous = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(previous);

        try
        {
            using (queue.SynchronizationContext.Install())
            {
                Assert.Same(queue.SynchronizationContext, SynchronizationContext.Current);
            }

            Assert.Same(previous, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public void InstallIsANoOpWhenTheSameContextIsAlreadyCurrent()
    {
        var queue = CreateQueue();

        try
        {
            using var outer = queue.SynchronizationContext.Install();
            var beforeNested = SynchronizationContext.Current;

            using (queue.SynchronizationContext.Install())
            {
                // Nested install for the same underlying queue must not replace the context,
                // since disposing an "already installed" scope must not undo the outer install.
                Assert.Same(beforeNested, SynchronizationContext.Current);
            }

            Assert.Same(beforeNested, SynchronizationContext.Current);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }
    }

    [Fact]
    public void CreateCopySharesTheSameUnderlyingSchedulerAsTheOriginal()
    {
        var queue = CreateQueue();
        var copy = (SimulationSynchronizationContext)queue.SynchronizationContext.CreateCopy();

        Assert.True(queue.SynchronizationContext.IsSameScheduler(copy));
        Assert.True(copy.IsSameScheduler(queue.SynchronizationContext));
    }

    [Fact]
    public void ContextsBackedByDifferentQueuesAreNotTheSameScheduler()
    {
        var first = CreateQueue();
        var second = CreateQueue();

        Assert.False(first.SynchronizationContext.IsSameScheduler(second.SynchronizationContext));
    }

    [Fact]
    public void SendAlwaysThrowsBecauseSynchronousExecutionIsNotSupported()
    {
        var queue = CreateQueue();

        Assert.Throws<InvalidOperationException>(() => queue.SynchronizationContext.Send(_ => { }, null));
    }

    [Fact]
    public void PostQueuesTheCallbackInsteadOfRunningItImmediately()
    {
        var queue = CreateQueue();
        var executed = false;

        queue.SynchronizationContext.Post(_ => executed = true, null);

        Assert.False(executed);
        Assert.True(queue.RunOnce());
        Assert.True(executed);
    }

    private static SimulationTaskQueue CreateQueue() => new(new SimulationClock(DateTimeOffset.UnixEpoch), new SingleThreadedGuard());
}
