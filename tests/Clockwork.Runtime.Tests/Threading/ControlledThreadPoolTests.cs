using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledThreadPool"/> shims: <c>QueueUserWorkItem</c> /
/// <c>UnsafeQueueUserWorkItem</c> queue their callback as a fresh controlled operation (run when the loop
/// is pumped, not inline), the safe variants flow the caller's <see cref="ExecutionContext"/> while the
/// unsafe variants do not, the native-overlapped surface is rejected precisely, and outside a simulation
/// every shim delegates to the real API.
/// </summary>
public sealed class ControlledThreadPoolTests
{
    private sealed class RecordingWorkItem : IThreadPoolWorkItem
    {
        public bool Executed { get; private set; }

        public void Execute() => Executed = true;
    }

    [Fact]
    public void QueueUserWorkItemQueuesCallbackAndRunsWhenPumped()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ran = false;
            var accepted = ControlledThreadPool.QueueUserWorkItem(_ => ran = true);

            Assert.True(accepted);
            Assert.False(ran); // queued, not run inline.

            coordinator.Loop.RunUntilIdle();
            Assert.True(ran);
        });
    }

    [Fact]
    public void QueueUserWorkItemPassesState()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            object? seen = null;
            ControlledThreadPool.QueueUserWorkItem(s => seen = s, "payload");
            coordinator.Loop.RunUntilIdle();
            Assert.Equal("payload", seen);
        });
    }

    [Fact]
    public void GenericQueueUserWorkItemPassesTypedState()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var seen = 0;
            ControlledThreadPool.QueueUserWorkItem(s => seen = s, 42, preferLocal: true);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(42, seen);
        });
    }

    [Fact]
    public void UnsafeQueueUserWorkItemRunsWorkItem()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var item = new RecordingWorkItem();
            ControlledThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
            Assert.False(item.Executed);

            coordinator.Loop.RunUntilIdle();
            Assert.True(item.Executed);
        });
    }

    [Fact]
    public void UnsafeGenericQueueUserWorkItemPassesTypedState()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var seen = 0;
            ControlledThreadPool.UnsafeQueueUserWorkItem(s => seen = s, 7, preferLocal: false);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(7, seen);
        });
    }

    [Fact]
    public void SafeQueueFlowsCapturedExecutionContext()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ambient = new AsyncLocal<int> { Value = 5 };
            var seen = -1;

            // Safe QueueUserWorkItem captures the caller's ExecutionContext at enqueue time.
            ControlledThreadPool.QueueUserWorkItem(_ => seen = ambient.Value);

            // Mutating the ambient value after enqueue must not affect the flowed snapshot.
            ambient.Value = 9;
            coordinator.Loop.RunUntilIdle();

            Assert.Equal(5, seen);
        });
    }

    [Fact]
    public void UnsafeQueueDoesNotFlowCapturedExecutionContext()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ambient = new AsyncLocal<int> { Value = 5 };
            var seen = -1;

            // Unsafe variant does not capture the caller's context; it observes the ambient value at run
            // time, so the post-enqueue mutation is visible - the flow distinction from the safe variant.
            ControlledThreadPool.UnsafeQueueUserWorkItem(_ => seen = ambient.Value, state: null);

            ambient.Value = 9;
            coordinator.Loop.RunUntilIdle();

            Assert.Equal(9, seen);
        });
    }

    [Fact]
    public void RejectNativeOverlappedThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledThreadPoolUnsupportedException>(
                () => ControlledThreadPool.RejectNativeOverlapped(
                    "System.Threading.ThreadPool.UnsafeQueueNativeOverlapped"));
            Assert.Contains("UnsafeQueueNativeOverlapped", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void OutsideSimulationQueueDelegatesToRealThreadPool()
    {
        using var completed = new ManualResetEventSlim(false);
        var accepted = ControlledThreadPool.QueueUserWorkItem(_ => completed.Set());

        Assert.True(accepted);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }
}
