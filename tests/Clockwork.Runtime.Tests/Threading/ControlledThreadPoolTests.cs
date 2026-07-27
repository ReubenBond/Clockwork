using System.Collections.Generic;
using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledThreadPool"/> shims: <c>QueueUserWorkItem</c> /
/// <c>UnsafeQueueUserWorkItem</c> queue their callback as a fresh controlled operation (run when the loop
/// is pumped, not inline), the safe variants flow the caller's <see cref="ExecutionContext"/> while the
/// unsafe variants do not, the native-overlapped surface is rejected precisely, the registered-wait
/// factories fire their <see cref="WaitOrTimerCallback"/> as a controlled operation on signal
/// (<c>timedOut: false</c>) or virtual-time timeout (<c>timedOut: true</c>) honouring executeOnlyOnce /
/// repeating and <c>Unregister</c>, and outside a simulation every shim delegates to the real API.
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
    public void RegisteredWaitFiresOnceOnSignalWithTimedOutFalse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var fires = new List<bool>();

            ControlledRegisteredWaitHandle reg = ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (_, timedOut) => fires.Add(timedOut), state: null, Timeout.Infinite, executeOnlyOnce: true);

            coordinator.Loop.RunUntilIdle();
            Assert.Empty(fires); // Armed but blocked: no signal yet.

            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal([false], fires); // Signalled => timedOut false.

            // executeOnlyOnce: a second signal must not fire the callback again.
            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal([false], fires);

            Assert.True(reg.Unregister(null));
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void RegisteredWaitFiresOnTimeoutWithTimedOutTrue()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var fires = new List<bool>();

            ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (_, timedOut) => fires.Add(timedOut), state: null, 100, executeOnlyOnce: true);

            // The virtual-time deadline elapses with no signal => the callback fires with timedOut true.
            DrainWithTime(coordinator);
            Assert.Equal([true], fires);
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void RepeatingRegisteredWaitFiresEachSignalUntilUnregister()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var count = 0;

            ControlledRegisteredWaitHandle reg = ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (_, _) => count++, state: null, Timeout.Infinite, executeOnlyOnce: false);

            coordinator.Loop.RunUntilIdle();

            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(1, count);

            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(2, count); // Re-armed and fired again.

            Assert.True(reg.Unregister(null));
            coordinator.Loop.RunUntilIdle();

            // After Unregister a further signal is not delivered.
            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(2, count);
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void UnregisterSignalsCompletionHandleWhenProvided()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            ManualResetEvent done = ControlledEventWaitHandle.CreateManualResetEvent(initialState: false);

            ControlledRegisteredWaitHandle reg = ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (_, _) => { }, state: null, Timeout.Infinite, executeOnlyOnce: false);

            coordinator.Loop.RunUntilIdle();

            Assert.True(reg.Unregister(done));
            coordinator.Loop.RunUntilIdle();

            // The completion handle is signalled once the registration has stopped firing.
            Assert.True(ControlledWaitHandle.WaitOne(done, 0));
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void SafeRegisterFlowsCapturedExecutionContext()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ambient = new AsyncLocal<int> { Value = 5 };
            var seen = -1;
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);

            // Safe RegisterWaitForSingleObject captures the caller's ExecutionContext at registration time.
            ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (_, _) => seen = ambient.Value, state: null, Timeout.Infinite, executeOnlyOnce: true);

            ambient.Value = 9; // Post-registration mutation must not affect the flowed snapshot.
            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();

            Assert.Equal(5, seen);
        });
    }

    [Fact]
    public void UnsafeRegisterDoesNotFlowCapturedExecutionContext()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ambient = new AsyncLocal<int> { Value = 5 };
            var seen = -1;
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);

            // Unsafe variant does not capture the caller's context; the callback observes the ambient value
            // at run time, so the post-registration mutation is visible.
            ControlledThreadPool.UnsafeRegisterWaitForSingleObject(
                evt, (_, _) => seen = ambient.Value, state: null, Timeout.Infinite, executeOnlyOnce: true);

            ambient.Value = 9;
            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();

            Assert.Equal(9, seen);
        });
    }

    [Fact]
    public void RegisterWaitPassesState()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            object? seen = null;

            ControlledThreadPool.RegisterWaitForSingleObject(
                evt, (s, _) => seen = s, state: "payload", Timeout.Infinite, executeOnlyOnce: true);

            ControlledEventWaitHandle.Set(evt);
            coordinator.Loop.RunUntilIdle();

            Assert.Equal("payload", seen);
        });
    }

    [Fact]
    public void OutsideSimulationRegisterWaitDelegatesToRealThreadPool()
    {
        using var evt = new ManualResetEvent(false);
        using var fired = new ManualResetEventSlim(false);

        ControlledRegisteredWaitHandle reg = ControlledThreadPool.RegisterWaitForSingleObject(
            evt, (_, _) => fired.Set(), state: null, Timeout.Infinite, executeOnlyOnce: true);

        evt.Set();
        Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        reg.Unregister(null);
    }

    [Fact]
    public void OutsideSimulationQueueDelegatesToRealThreadPool()
    {
        using var completed = new ManualResetEventSlim(false);
        var accepted = ControlledThreadPool.QueueUserWorkItem(_ => completed.Set());

        Assert.True(accepted);
        Assert.True(completed.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // Pumps the loop and folds virtual-time deadlines in, mirroring a host drive loop: run ready work to
    // idle, then advance to the next pending deadline (firing timeouts) and pump again, until quiescent.
    private static void DrainWithTime(ControlledTaskLoopCoordinator coordinator)
    {
        while (true)
        {
            coordinator.Loop.RunUntilIdle();
            TimeSpan? due = coordinator.Loop.NextDeadlineDue();
            if (due is null)
            {
                return;
            }

            coordinator.Loop.AdvanceTimeTo(due.Value);
        }
    }
}
