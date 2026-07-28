using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

public sealed class ControlledCancellationTokenSourceTests
{
    [Fact]
    public void TimedConstructorCancelsAtVirtualDeadline()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using CancellationTokenSource source =
                ControlledCancellationTokenSource.Create(TimeSpan.FromMilliseconds(25));
            Assert.False(source.IsCancellationRequested);

            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(25));
            coordinator.Loop.RunUntilIdle();

            Assert.True(source.IsCancellationRequested);
        });
    }

    [Fact]
    public void ZeroConstructorIsCanceledSynchronously()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using CancellationTokenSource source = ControlledCancellationTokenSource.Create(0);
            Assert.True(source.IsCancellationRequested);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
        });
    }

    [Fact]
    public void CancelAfterZeroIsQueuedAndCanBeResetBeforeItRuns()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var source = new CancellationTokenSource();
            ControlledCancellationTokenSource.CancelAfter(source, 0);
            ControlledCancellationTokenSource.CancelAfter(source, 10);
            coordinator.Loop.RunUntilIdle();
            Assert.False(source.IsCancellationRequested);

            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(10));
            coordinator.Loop.RunUntilIdle();
            Assert.True(source.IsCancellationRequested);
        });
    }

    [Fact]
    public void CancelAfterResetAndDisableRemoveStaleDeadlines()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var source = new CancellationTokenSource();
            ControlledCancellationTokenSource.CancelAfter(source, 10);
            ControlledCancellationTokenSource.CancelAfter(source, 20);
            Assert.Equal(TimeSpan.FromMilliseconds(20), coordinator.Loop.NextDeadlineDue());

            ControlledCancellationTokenSource.CancelAfter(source, Timeout.Infinite);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(100));
            Assert.False(source.IsCancellationRequested);
        });
    }

    [Fact]
    public void ManualCancelDisablesTimerAndRunsCallbacksSynchronously()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var source = new CancellationTokenSource();
            var callbackRan = false;
            using CancellationTokenRegistration registration =
                source.Token.Register(() => callbackRan = true);
            ControlledCancellationTokenSource.CancelAfter(source, 10);

            ControlledCancellationTokenSource.Cancel(source);

            Assert.True(callbackRan);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
        });
    }

    [Fact]
    public void DisposeSuppressesPendingTimerCancellation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            ControlledCancellationTokenSource.CancelAfter(source, 10);
            ControlledCancellationTokenSource.Dispose(source);

            Assert.Null(coordinator.Loop.NextDeadlineDue());
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(10));
            Assert.False(token.IsCancellationRequested);
        });
    }

    [Fact]
    public void CancelAfterOnDisposedSourceThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new CancellationTokenSource();
            source.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => ControlledCancellationTokenSource.CancelAfter(source, 1));
        });
    }

    [Fact]
    public void CancelAsyncRunsOnControlledQueue()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var source = new CancellationTokenSource();
            Task cancellation = ControlledCancellationTokenSource.CancelAsync(source);
            Assert.False(cancellation.IsCompleted);

            coordinator.Loop.RunUntil(() => cancellation.IsCompleted, "test.cancel");

            Assert.True(cancellation.IsCompletedSuccessfully);
            Assert.True(source.IsCancellationRequested);
        });
    }

    [Fact]
    public void LinkedCancellationDisablesPendingCancelAfterDeadline()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var parent = new CancellationTokenSource();
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(parent.Token);
            ControlledCancellationTokenSource.CancelAfter(linked, 10);

            parent.Cancel();

            Assert.True(linked.IsCancellationRequested);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
        });
    }
}
