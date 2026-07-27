using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

// The controlled shims mirror the BCL surface, so most methods have a CancellationToken overload. These
// tests intentionally exercise the specific overloads (including token-less and explicit-token ones); the
// TestContext token guidance does not apply to the deterministic simulation loop.
#pragma warning disable xUnit1051

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledSemaphoreSlim"/> shims: the count and waiter set are
/// modelled on the cooperative logical thread, a contended synchronous <c>Wait</c> pumps the loop until
/// a permit is released, <c>WaitAsync</c> completes when a permit is released, max-count is enforced,
/// cancellation is honoured, and <c>AvailableWaitHandle</c> is rejected until Phase 7B. Outside a
/// simulation every shim delegates to the real <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class ControlledSemaphoreSlimTests
{
    [Fact]
    public void WaitDecrementsAndReleaseIncrements()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(2);

            Assert.Equal(2, ControlledSemaphoreSlim.CurrentCount(sem));
            ControlledSemaphoreSlim.Wait(sem);
            Assert.Equal(1, ControlledSemaphoreSlim.CurrentCount(sem));
            ControlledSemaphoreSlim.Wait(sem);
            Assert.Equal(0, ControlledSemaphoreSlim.CurrentCount(sem));

            var previous = ControlledSemaphoreSlim.Release(sem);
            Assert.Equal(0, previous);
            Assert.Equal(1, ControlledSemaphoreSlim.CurrentCount(sem));
        });
    }

    [Fact]
    public void WaitWithZeroTimeoutFailsWhenEmpty()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(0);
            Assert.False(ControlledSemaphoreSlim.Wait(sem, 0));
        });
    }

    [Fact]
    public void ReleaseBeyondMaxCountThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(1, 1);
            Assert.Throws<SemaphoreFullException>(() => ControlledSemaphoreSlim.Release(sem));
        });
    }

    [Fact]
    public void ReleaseInvalidCountThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(0);
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlledSemaphoreSlim.Release(sem, 0));
        });
    }

    [Fact]
    public void ContendedWaitProceedsAfterRelease()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(0);
            var log = new List<int>();

            var consumer = ControlledThread.Create(() =>
            {
                ControlledSemaphoreSlim.Wait(sem);
                log.Add(2);
            });
            ControlledThread.Start(consumer);

            log.Add(1);
            ControlledSemaphoreSlim.Release(sem);
            ControlledThread.Join(consumer);
            log.Add(3);

            Assert.Equal("1,2,3", string.Join(",", log));
        });
    }

    [Fact]
    public void WaitAsyncCompletesWhenPermitReleased()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(0);

            // With no permit available the task is pending; a Release completes it synchronously on the
            // logical thread (which is exactly what a loop-driven await observes).
            var task = ControlledSemaphoreSlim.WaitAsync(sem);
            Assert.False(task.IsCompleted);

            ControlledSemaphoreSlim.Release(sem);
            Assert.True(task.IsCompleted);
            Assert.Equal(0, ControlledSemaphoreSlim.CurrentCount(sem));
        });
    }

    [Fact]
    public void WaitAsyncAlreadyAvailableCompletesSynchronously()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(1);
            var task = ControlledSemaphoreSlim.WaitAsync(sem, 0);
            Assert.True(task.IsCompleted);
            Assert.True(task.Result);
            Assert.Equal(0, ControlledSemaphoreSlim.CurrentCount(sem));
        });
    }

    [Fact]
    public void WaitCancellationThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(0);
            using var cts = new CancellationTokenSource();
            Exception? caught = null;

            var waiter = ControlledThread.Create(() =>
            {
                try
                {
                    ControlledSemaphoreSlim.Wait(sem, cts.Token);
                }
                catch (Exception exception)
                {
                    caught = exception;
                }
            });
            ControlledThread.Start(waiter);

            var canceller = ControlledThread.Create(() => cts.Cancel());
            ControlledThread.Start(canceller);

            ControlledThread.Join(canceller);
            ControlledThread.Join(waiter);

            Assert.IsAssignableFrom<OperationCanceledException>(caught);
        });
    }

    [Fact]
    public void WaitAfterDisposeThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(1);
            ControlledSemaphoreSlim.Dispose(sem);
            Assert.Throws<ObjectDisposedException>(() => ControlledSemaphoreSlim.Wait(sem));
        });
    }

    [Fact]
    public void AvailableWaitHandleIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sem = ControlledSemaphoreSlim.Create(1);
            Assert.Throws<ControlledSemaphoreSlimUnsupportedException>(
                () => ControlledSemaphoreSlim.AvailableWaitHandle(sem));
        });
    }

    [Fact]
    public void OutsideSimulationDelegatesToRealSemaphore()
    {
        var sem = ControlledSemaphoreSlim.Create(1);
        Assert.Equal(1, ControlledSemaphoreSlim.CurrentCount(sem));
        Assert.True(ControlledSemaphoreSlim.Wait(sem, 0));
        Assert.Equal(0, ControlledSemaphoreSlim.CurrentCount(sem));
        ControlledSemaphoreSlim.Release(sem);
        Assert.Equal(1, ControlledSemaphoreSlim.CurrentCount(sem));
        ControlledSemaphoreSlim.Dispose(sem);
    }
}
