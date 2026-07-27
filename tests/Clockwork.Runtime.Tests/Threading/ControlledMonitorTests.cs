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
    }
}
