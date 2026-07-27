using System.Threading;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

/// <summary>
/// Tests for the controlled <see cref="ControlledThread"/> shims: a controlled thread is a real
/// <see cref="Thread"/> object whose body is queued as a fresh controlled operation, <c>Join</c> pumps
/// the deterministic loop instead of blocking, the static <c>Sleep</c>/<c>Yield</c>/<c>SpinWait</c> hints
/// are cooperative no-ops that never consume real time, and the OS-specific surface (priority, apartment
/// state, interrupt) is rejected precisely. Outside a simulation every shim delegates to the real API.
/// </summary>
public sealed class ControlledThreadTests
{
    [Fact]
    public void StartQueuesBodyAndJoinObservesCompletion()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ran = false;
            var thread = ControlledThread.Create(() => ran = true);

            // The body is queued as controlled work, not run inline by Start.
            ControlledThread.Start(thread);
            Assert.False(ran);

            // Join pumps the deterministic loop until the body completes rather than blocking a thread.
            ControlledThread.Join(thread);
            Assert.True(ran);
        });
    }

    [Fact]
    public void StartOfParameterizedThreadPassesArgument()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            object? captured = null;
            var thread = ControlledThread.Create((ParameterizedThreadStart)(o => captured = o));

            ControlledThread.Start(thread, "payload");
            ControlledThread.Join(thread);

            Assert.Equal("payload", captured);
        });
    }

    [Fact]
    public void MultipleJoinsAllObserveCompletion()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var count = 0;
            var thread = ControlledThread.Create(() => count++);

            ControlledThread.Start(thread);
            ControlledThread.Join(thread);
            ControlledThread.Join(thread);
            ControlledThread.Join(thread);

            // The body runs exactly once; every Join simply observes the already-terminated thread.
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public void JoinWithTimeoutReturnsTrueOnCompletion()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ran = false;
            var thread = ControlledThread.Create(() => ran = true);
            ControlledThread.Start(thread);

            Assert.True(ControlledThread.Join(thread, 1000));
            Assert.True(ControlledThread.Join(thread, TimeSpan.FromSeconds(1)));
            Assert.True(ran);
        });
    }

    [Fact]
    public void JoinObservesFaultedThreadWithoutThrowingOrHanging()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var reached = false;
            var thread = ControlledThread.Create(() =>
            {
                reached = true;
                throw new InvalidOperationException("boom");
            });

            ControlledThread.Start(thread);

            // Deviation from real threads (whose unhandled exception crashes the process): the fault is
            // captured as the thread's completion, so Join observes deterministic termination instead of
            // tearing down the host or hanging.
            ControlledThread.Join(thread);
            Assert.True(reached);
        });
    }

    [Fact]
    public void StartedThreadRejectsSecondStart()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            ControlledThread.Start(thread);

            Assert.Throws<ThreadStateException>(() => ControlledThread.Start(thread));
        });
    }

    [Fact]
    public void StartOfUnregisteredThreadIsRejected()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // A thread not created through the controlled surface has an unknown body; starting the real OS
            // thread would escape the single logical thread, so it is rejected precisely.
            var raw = new Thread(() => { });
            Assert.Throws<ControlledThreadUnsupportedException>(() => ControlledThread.Start(raw));
        });
    }

    [Fact]
    public void SleepYieldAndSpinWaitAreCooperativeNoOps()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // None of these block or consume real time inside a simulation.
            ControlledThread.Sleep(10_000);
            ControlledThread.Sleep(TimeSpan.FromHours(1));
            ControlledThread.SpinWait(1_000_000);
            Assert.False(ControlledThread.Yield());
        });
    }

    [Fact]
    public void SetPriorityIsRejectedUnderSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            var ex = Assert.Throws<ControlledThreadUnsupportedException>(
                () => ControlledThread.SetPriority(thread, ThreadPriority.Highest));
            Assert.Contains("set_Priority", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void InterruptIsRejectedUnderSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            Assert.Throws<ControlledThreadUnsupportedException>(() => ControlledThread.Interrupt(thread));
        });
    }

    [Fact]
    public void ApartmentStateIsRejectedUnderSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            Assert.Throws<ControlledThreadUnsupportedException>(
                () => ControlledThread.SetApartmentState(thread, ApartmentState.STA));
            Assert.Throws<ControlledThreadUnsupportedException>(
                () => ControlledThread.TrySetApartmentState(thread, ApartmentState.STA));
        });
    }

    [Fact]
    public void CreatePreservesLogicalIdentitySurface()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            thread.Name = "worker";
            thread.IsBackground = true;

            // The controlled thread is a real Thread object, so its logical identity keeps working.
            Assert.Equal("worker", thread.Name);
            Assert.True(thread.IsBackground);
            Assert.True(thread.ManagedThreadId > 0);
        });
    }

    [Fact]
    public void OutsideSimulationYieldDelegatesToRealApi()
    {
        // No active simulation: the shim must delegate to the real BCL API unchanged.
        _ = ControlledThread.Yield();

        var ran = false;
        var thread = ControlledThread.Create(() => ran = true);
        ControlledThread.Start(thread);
        ControlledThread.Join(thread);

        Assert.True(ran);
    }
}
