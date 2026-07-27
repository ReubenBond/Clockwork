using System;
using System.Collections.Generic;
using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled event / wait-handle surface (<see cref="ControlledEventWaitHandle"/> and
/// <see cref="ControlledWaitHandle"/>). Inside a simulation the signalled state and its deterministic FIFO
/// waiter set are modelled on the cooperative logical thread: a <see cref="WaitHandle.WaitOne()"/> with no
/// signal pumps the loop until <c>Set</c>, an auto-reset event wakes exactly one waiter while a manual-reset
/// event releases all and stays signalled, and finite timeouts consume only virtual time. Named /
/// cross-process APIs and the raw handle accessors are rejected precisely. Outside a simulation every shim
/// delegates to the real BCL primitive.
/// </summary>
public sealed class ControlledEventWaitHandleTests
{
    // ---- construction + immediate (zero-wait) semantics ----

    [Fact]
    public void AutoResetEventInitiallySignalledConsumesOnce()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: true);

            // The single signal is consumed by the first WaitOne; the second times out immediately.
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0));
            Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
        });
    }

    [Fact]
    public void ManualResetEventInitiallySignalledStaysSignalled()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ManualResetEvent evt = ControlledEventWaitHandle.CreateManualResetEvent(initialState: true);

            // A manual-reset event remains signalled until Reset, so repeated waits all succeed.
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0));
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0));

            Assert.True(ControlledEventWaitHandle.Reset(evt));
            Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
        });
    }

    [Fact]
    public void AutoResetEventSetThenWaitConsumesSignal()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);

            Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
            Assert.True(ControlledEventWaitHandle.Set(evt));
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0));
            Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
        });
    }

    // ---- cross-strand signal wakes a blocked waiter ----

    [Fact]
    public void AutoResetSetWakesExactlyOneWaiter()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var woke = new List<int>();

            var first = ControlledThread.Create(() =>
            {
                ControlledWaitHandle.WaitOne(evt);
                woke.Add(1);
            });
            var second = ControlledThread.Create(() =>
            {
                ControlledWaitHandle.WaitOne(evt);
                woke.Add(2);
            });

            ControlledThread.Start(first);
            ControlledThread.Start(second);

            // A single Set releases exactly one waiter (FIFO: the first to block).
            ControlledEventWaitHandle.Set(evt);
            ControlledThread.Join(first);

            Assert.Equal([1], woke);

            // The second waiter is still blocked; a second Set releases it.
            ControlledEventWaitHandle.Set(evt);
            ControlledThread.Join(second);

            Assert.Equal([1, 2], woke);
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void ManualResetSetReleasesAllWaiters()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ManualResetEvent evt = ControlledEventWaitHandle.CreateManualResetEvent(initialState: false);
            var woke = new List<int>();

            var first = ControlledThread.Create(() =>
            {
                ControlledWaitHandle.WaitOne(evt);
                woke.Add(1);
            });
            var second = ControlledThread.Create(() =>
            {
                ControlledWaitHandle.WaitOne(evt);
                woke.Add(2);
            });

            ControlledThread.Start(first);
            ControlledThread.Start(second);

            // A single Set releases every waiter and stays signalled.
            ControlledEventWaitHandle.Set(evt);
            ControlledThread.Join(first);
            ControlledThread.Join(second);

            Assert.Equal([1, 2], woke);
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0)); // Still signalled.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    // ---- finite virtual-time timeouts / signal-vs-timeout races ----

    [Fact]
    public void WaitFiniteTimesOutWhenNeverSet()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);

            Assert.False(ControlledWaitHandle.WaitOne(evt, 100));
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFiniteSetBeforeDeadlineSucceeds()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var signalled = false;

            var waiter = ControlledThread.Create(() => signalled = ControlledWaitHandle.WaitOne(evt, 500));
            var setter = ControlledThread.Create(() =>
                VirtualDelayThenRun(100, () => ControlledEventWaitHandle.Set(evt)));

            ControlledThread.Start(waiter);
            ControlledThread.Start(setter);
            ControlledThread.Join(waiter);
            ControlledThread.Join(setter);

            Assert.True(signalled); // Set at 100 (< 500) beat the timeout.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFiniteSetAfterDeadlineTimesOut()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            var signalled = true;

            var waiter = ControlledThread.Create(() => signalled = ControlledWaitHandle.WaitOne(evt, 100));
            var setter = ControlledThread.Create(() =>
                VirtualDelayThenRun(500, () => ControlledEventWaitHandle.Set(evt)));

            ControlledThread.Start(waiter);
            ControlledThread.Start(setter);
            ControlledThread.Join(waiter);
            ControlledThread.Join(setter);

            Assert.False(signalled); // Timed out at 100 before the Set at 500.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitOneTimeSpanOverloadHonoursVirtualDeadline()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            Assert.False(ControlledWaitHandle.WaitOne(evt, TimeSpan.FromMilliseconds(100)));
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    // ---- disposal ----

    [Fact]
    public void WaitAfterDisposeThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            ControlledWaitHandle.Dispose(evt);
            Assert.Throws<ObjectDisposedException>(() => ControlledWaitHandle.WaitOne(evt, 0));
        });
    }

    [Fact]
    public void SetAfterDisposeThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ManualResetEvent evt = ControlledEventWaitHandle.CreateManualResetEvent(initialState: false);
            ControlledWaitHandle.Close(evt);
            Assert.Throws<ObjectDisposedException>(() => ControlledEventWaitHandle.Set(evt));
        });
    }

    // ---- invalid input ----

    [Fact]
    public void WaitOneRejectsInvalidTimeout()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            Assert.Throws<ArgumentOutOfRangeException>(() => ControlledWaitHandle.WaitOne(evt, -2));
        });
    }

    // ---- named / cross-process + raw handle rejections ----

    [Fact]
    public void NamedEventConstructorIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() =>
                ControlledEventWaitHandle.CreateNamedEvent(false, EventResetMode.AutoReset, "clockwork-named"));
        });
    }

    [Fact]
    public void NullNamedEventConstructorIsControlled()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // A null name is a degenerate unnamed event, which is fully controlled.
            EventWaitHandle evt = ControlledEventWaitHandle.CreateNamedEvent(true, EventResetMode.ManualReset, name: null);
            Assert.True(ControlledWaitHandle.WaitOne(evt, 0));
        });
    }

    [Fact]
    public void OpenExistingIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() =>
                ControlledEventWaitHandle.OpenExisting("clockwork-named"));
        });
    }

    [Fact]
    public void TryOpenExistingIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() =>
                ControlledEventWaitHandle.TryOpenExisting("clockwork-named", out _));
        });
    }

    [Fact]
    public void RawHandleAccessorsAreRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            AutoResetEvent evt = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() => ControlledWaitHandle.GetHandle(evt));
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() => ControlledWaitHandle.GetSafeWaitHandle(evt));
        });
    }

    [Fact]
    public void WaitingOnUncontrolledHandleIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // An event constructed directly (not via a Create factory) has no modelled state.
            using var raw = new AutoResetEvent(false);
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() => ControlledWaitHandle.WaitOne(raw, 0));
        });
    }

    // ---- outside a simulation everything delegates to the real BCL primitive ----

    [Fact]
    public void OutsideSimulationDelegatesToRealEvent()
    {
        ManualResetEvent evt = ControlledEventWaitHandle.CreateManualResetEvent(initialState: false);
        Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
        Assert.True(ControlledEventWaitHandle.Set(evt));
        Assert.True(ControlledWaitHandle.WaitOne(evt, 0)); // Manual-reset stays signalled.
        Assert.True(ControlledEventWaitHandle.Reset(evt));
        Assert.False(ControlledWaitHandle.WaitOne(evt, 0));
        ControlledWaitHandle.Dispose(evt);
    }

    // A "virtual delay" built from a never-set finite Wait sequences an action at a chosen virtual instant,
    // with no wall-clock time anywhere, for exact before/at/after timeout coverage.
    private static void VirtualDelayThenRun(int delayMilliseconds, Action action)
    {
        AutoResetEvent timer = ControlledEventWaitHandle.CreateAutoResetEvent(initialState: false);
        Assert.False(ControlledWaitHandle.WaitOne(timer, delayMilliseconds));
        action();
    }
}
