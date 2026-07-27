using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledMonitor"/> shims: mutual exclusion, reentrancy and
/// ownership are tracked against the cooperative logical strand rather than a physical thread; a
/// contended acquire pumps the deterministic loop instead of blocking an OS thread; and
/// <c>Wait</c>/<c>Pulse</c>/<c>PulseAll</c> implement the condition-variable protocol. Outside a
/// simulation every shim delegates to the real BCL <see cref="Monitor"/>.
/// </summary>
public sealed class ControlledMonitorTests
{
    [Fact]
    public void EnterExitTracksOwnershipAndReentrancy()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();

            Assert.False(ControlledMonitor.IsEntered(gate));
            ControlledMonitor.Enter(gate);
            Assert.True(ControlledMonitor.IsEntered(gate));

            // Reentrant acquire by the same strand just deepens the recursion count.
            ControlledMonitor.Enter(gate);
            ControlledMonitor.Exit(gate);
            Assert.True(ControlledMonitor.IsEntered(gate));

            ControlledMonitor.Exit(gate);
            Assert.False(ControlledMonitor.IsEntered(gate));
        });
    }

    [Fact]
    public void ExitWithoutOwnershipThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<SynchronizationLockException>(() => ControlledMonitor.Exit(new object()));
        });
    }

    [Fact]
    public void EnterNullThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => ControlledMonitor.Enter(null!));
    }

    [Fact]
    public void EnterWithLockTakenTrueThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var taken = true;
            Assert.Throws<ArgumentException>(() =>
            {
                ControlledMonitor.Enter(gate, ref taken);
            });
        });
    }

    [Fact]
    public void EnterRefBoolSetsLockTaken()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var taken = false;
            ControlledMonitor.Enter(gate, ref taken);
            Assert.True(taken);
            Assert.True(ControlledMonitor.IsEntered(gate));
            ControlledMonitor.Exit(gate);
        });
    }

    [Fact]
    public void TryEnterInvalidTimeoutThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ControlledMonitor.TryEnter(new object(), -2));
    }

    [Fact]
    public void TryEnterContendedReturnsFalse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var tookIt = true;

            ControlledMonitor.Enter(gate);
            var contender = ControlledThread.Create(() => tookIt = ControlledMonitor.TryEnter(gate));
            ControlledThread.Start(contender);

            // The contender runs a non-blocking TryEnter while the root strand still owns the lock.
            ControlledThread.Join(contender);
            ControlledMonitor.Exit(gate);

            Assert.False(tookIt);
        });
    }

    [Fact]
    public void ContendedEnterAcquiresAfterOwnerReleases()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var log = new List<int>();

            ControlledMonitor.Enter(gate);
            var contender = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(gate);
                log.Add(2);
                ControlledMonitor.Exit(gate);
            });
            ControlledThread.Start(contender);

            log.Add(1);
            ControlledMonitor.Exit(gate);
            ControlledThread.Join(contender);
            log.Add(3);

            Assert.Equal("1,2,3", string.Join(",", log));
        });
    }

    [Fact]
    public void WaitReleasesAndReacquiresAfterPulse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var woke = false;

            var waiter = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.Wait(mon);
                woke = true;
                ControlledMonitor.Exit(mon);
            });
            var signaller = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.Pulse(mon);
                ControlledMonitor.Exit(mon);
            });

            ControlledThread.Start(waiter);
            ControlledThread.Start(signaller);

            ControlledThread.Join(waiter);
            ControlledThread.Join(signaller);

            Assert.True(woke);
        });
    }

    [Fact]
    public void PulseAllWakesEveryWaiter()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var woken = 0;

            Thread MakeWaiter() => ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.Wait(mon);
                woken++;
                ControlledMonitor.Exit(mon);
            });

            var w1 = MakeWaiter();
            var w2 = MakeWaiter();
            var signaller = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.PulseAll(mon);
                ControlledMonitor.Exit(mon);
            });

            ControlledThread.Start(w1);
            ControlledThread.Start(w2);
            ControlledThread.Start(signaller);

            ControlledThread.Join(w1);
            ControlledThread.Join(w2);
            ControlledThread.Join(signaller);

            Assert.Equal(2, woken);
        });
    }

    [Fact]
    public void PulseWithoutOwnershipThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<SynchronizationLockException>(() => ControlledMonitor.Pulse(new object()));
        });
    }

    [Fact]
    public void WaitWithoutOwnershipThrows()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<SynchronizationLockException>(() => ControlledMonitor.Wait(new object()));
        });
    }

    [Fact]
    public void UnpulsedWaitSurfacesAsDeadlock()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            ControlledMonitor.Enter(mon);

            // No other strand can ever pulse, so the parked wait can never make progress. On the single
            // logical thread that is recognised immediately as a deadlock instead of a real-time hang.
            Assert.Throws<ControlledSynchronousWaitDeadlockException>(() => ControlledMonitor.Wait(mon));
        });
    }

    [Fact]
    public void OutsideSimulationDelegatesToRealMonitor()
    {
        var gate = new object();

        ControlledMonitor.Enter(gate);
        Assert.True(ControlledMonitor.IsEntered(gate));
        Assert.True(Monitor.IsEntered(gate));
        ControlledMonitor.Exit(gate);
        Assert.False(ControlledMonitor.IsEntered(gate));
        Assert.Equal(Monitor.LockContentionCount, ControlledMonitor.LockContentionCount());
    }

    [Fact]
    public void LockContentionCountIsRejectedInsideSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledTaskUnsupportedException>(
                () => ControlledMonitor.LockContentionCount());
            Assert.Equal("System.Threading.Monitor.LockContentionCount", ex.ApiName);
        });
    }

    // ---- Finite virtual-time timeouts (Monitor.TryEnter / Monitor.Wait) ----
    //
    // Time only advances when nothing else can run, so a discrete event (a release, a pulse) always happens
    // before any timeout that needs a time advance. To sequence an event at a chosen virtual instant D these
    // tests build a deterministic "virtual delay" out of a finite Monitor.Wait that is never pulsed: it
    // parks, times out at exactly D, and its strand then performs the release/pulse. Comparing D to the
    // waiter's timeout T gives exact before/at/after-deadline coverage with no wall-clock time.

    private static void VirtualDelayThenRun(int delayMilliseconds, Action action)
    {
        var timer = new object();
        ControlledMonitor.Enter(timer);
        var pulsed = ControlledMonitor.Wait(timer, delayMilliseconds);
        Assert.False(pulsed); // Never pulsed, so it always elapses on its virtual deadline.
        ControlledMonitor.Exit(timer);
        action();
    }

    [Fact]
    public void TryEnterFiniteTimesOutWhenNeverReleased()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var acquired = true;

            ControlledMonitor.Enter(gate);
            var contender = ControlledThread.Create(() => acquired = ControlledMonitor.TryEnter(gate, 100));
            ControlledThread.Start(contender);

            // The root never releases before joining, so the contender parks and its virtual-time deadline
            // is the only event left: modelled time advances to it and the acquire returns false.
            ControlledThread.Join(contender);
            Assert.False(acquired);

            // The root still owns the lock; the timed-out acquire took nothing.
            Assert.True(ControlledMonitor.IsEntered(gate));
            ControlledMonitor.Exit(gate);
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void TryEnterFiniteRefBoolTimesOut()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var gate = new object();
            var taken = true;

            ControlledMonitor.Enter(gate);
            var contender = ControlledThread.Create(() =>
            {
                var t = false;
                ControlledMonitor.TryEnter(gate, 50, ref t);
                taken = t;
            });
            ControlledThread.Start(contender);
            ControlledThread.Join(contender);

            Assert.False(taken);
            ControlledMonitor.Exit(gate);
        });
    }

    [Fact]
    public void WaitFiniteTimesOutAndReacquiresMonitor()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var signalled = true;
            var ownedAfter = false;

            var waiter = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                signalled = ControlledMonitor.Wait(mon, 100);
                // Wait must reacquire the monitor before returning, even on timeout.
                ownedAfter = ControlledMonitor.IsEntered(mon);
                ControlledMonitor.Exit(mon);
            });

            ControlledThread.Start(waiter);
            ControlledThread.Join(waiter);

            Assert.False(signalled); // Timed out - no pulse ever arrived.
            Assert.True(ownedAfter); // Monitor was reacquired before the timed-out Wait returned.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFinitePulsedBeforeDeadlineReturnsTrue()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var signalled = false;

            var waiter = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                signalled = ControlledMonitor.Wait(mon, 500); // Deadline 500.
                ControlledMonitor.Exit(mon);
            });
            var signaller = ControlledThread.Create(() => VirtualDelayThenRun(100, () =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.Pulse(mon);
                ControlledMonitor.Exit(mon);
            }));

            ControlledThread.Start(waiter);
            ControlledThread.Start(signaller);
            ControlledThread.Join(waiter);
            ControlledThread.Join(signaller);

            Assert.True(signalled); // Pulse at 100 (< 500) beat the timeout.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFinitePulsedAfterDeadlineReturnsFalse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var signalled = true;

            var waiter = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                signalled = ControlledMonitor.Wait(mon, 100); // Deadline 100.
                ControlledMonitor.Exit(mon);
            });
            var signaller = ControlledThread.Create(() => VirtualDelayThenRun(500, () =>
            {
                ControlledMonitor.Enter(mon);
                // The waiter has already timed out and left, so this pulse selects no waiter (a late pulse
                // must not be consumed by a waiter that already gave up).
                ControlledMonitor.Pulse(mon);
                ControlledMonitor.Exit(mon);
            }));

            ControlledThread.Start(waiter);
            ControlledThread.Start(signaller);
            ControlledThread.Join(waiter);
            ControlledThread.Join(signaller);

            Assert.False(signalled); // Timed out at 100 before the pulse at 500.
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFiniteRestoresRecursionCountOnTimeout()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var mon = new object();
            var reentrantlyOwned = false;

            var waiter = ControlledThread.Create(() =>
            {
                ControlledMonitor.Enter(mon);
                ControlledMonitor.Enter(mon); // Recursion depth 2.
                ControlledMonitor.Wait(mon, 50); // Timeout: must restore the full depth-2 ownership.

                ControlledMonitor.Exit(mon);
                reentrantlyOwned = ControlledMonitor.IsEntered(mon); // Still owned after one Exit.
                ControlledMonitor.Exit(mon);
            });

            ControlledThread.Start(waiter);
            ControlledThread.Join(waiter);

            Assert.True(reentrantlyOwned);
            Assert.False(ControlledMonitor.IsEntered(mon));
            Assert.True(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void WaitFiniteAtDeadlineIsDeterministicTimeout()
    {
        // When a pulse and the wait's deadline are due at the same virtual instant, the earlier-registered
        // deadline fires first. The waiter parks (and registers) before the signaller's delay, so the
        // waiter times out - a deterministic, replayable tie-break rather than a real-time race.
        for (var seed = 1; seed <= 5; seed++)
        {
            var coordinator = new ControlledTaskLoopCoordinator();
            var signalled = true;

            TaskTestHarness.RunInSimulation(
                coordinator,
                () =>
                {
                    var mon = new object();
                    var waiter = ControlledThread.Create(() =>
                    {
                        ControlledMonitor.Enter(mon);
                        signalled = ControlledMonitor.Wait(mon, 100);
                        ControlledMonitor.Exit(mon);
                    });
                    var signaller = ControlledThread.Create(() => VirtualDelayThenRun(100, () =>
                    {
                        ControlledMonitor.Enter(mon);
                        ControlledMonitor.Pulse(mon);
                        ControlledMonitor.Exit(mon);
                    }));

                    ControlledThread.Start(waiter);
                    ControlledThread.Start(signaller);
                    ControlledThread.Join(waiter);
                    ControlledThread.Join(signaller);
                },
                runtime: TaskTestHarness.NewRuntime(seed));

            Assert.False(signalled);
        }
    }

    [Fact]
    public void RepeatedFiniteTimeoutsLeaveNoLeakedWaiters()
    {
        // Stress: the same finite-timeout scenario replayed many times is deterministic and leaves the loop
        // idle every time (no parked waiter or pending deadline leaks across iterations).
        for (var i = 0; i < 200; i++)
        {
            var coordinator = new ControlledTaskLoopCoordinator();
            var acquired = true;

            TaskTestHarness.RunInSimulation(coordinator, () =>
            {
                var gate = new object();
                ControlledMonitor.Enter(gate);
                var contender = ControlledThread.Create(() => acquired = ControlledMonitor.TryEnter(gate, 25));
                ControlledThread.Start(contender);
                ControlledThread.Join(contender);
                ControlledMonitor.Exit(gate);
            });

            Assert.False(acquired);
            Assert.True(coordinator.Loop.IsIdle);
        }
    }
}
