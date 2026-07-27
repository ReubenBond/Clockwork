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

    [Fact]
    public void JoinOnUnstartedThreadThrowsThreadStateException()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });

            var exception = Assert.Throws<ThreadStateException>(() => ControlledThread.Join(thread));

            Assert.IsType<ThreadStateException>(exception);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void JoinIntegerRejectsValuesLessThanInfinite(int millisecondsTimeout)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            ControlledThread.Start(thread);
            ControlledThread.Join(thread);

            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ControlledThread.Join(thread, millisecondsTimeout));

            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal("millisecondsTimeout", exception.ParamName);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(-2L)]
    [InlineData((long)int.MaxValue + 1)]
    public void JoinTimeSpanRejectsOutOfRangeValues(long milliseconds)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() => { });
            ControlledThread.Start(thread);
            ControlledThread.Join(thread);

            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ControlledThread.Join(thread, TimeSpan.FromMilliseconds(milliseconds)));

            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal("timeout", exception.ParamName);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void JoinZeroDoesNotPumpPendingThreadAndReportsCompletedThread()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var runs = 0;
            var thread = ControlledThread.Create(() => runs++);
            ControlledThread.Start(thread);

            Assert.False(ControlledThread.Join(thread, 0));
            Assert.Equal(0, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            Assert.Equal(1, coordinator.Loop.ReadyCount);
            Assert.Equal(0, coordinator.Loop.WaitingCount);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            Assert.False(coordinator.Loop.IsIdle);

            Assert.Equal(1, coordinator.Loop.RunUntilIdle());
            Assert.Equal(1, runs);
            Assert.True(ControlledThread.Join(thread, 0));
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteJoinCompletingBeforeDeadlineReturnsTrue(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            TimeSpan? completedAt = null;
            var thread = ControlledThread.Create(() => completedAt = coordinator.Loop.VirtualNow);
            ControlledThread.Start(thread);

            var joined = useTimeSpan
                ? ControlledThread.Join(thread, TimeSpan.FromMilliseconds(100))
                : ControlledThread.Join(thread, 100);

            Assert.True(joined);
            Assert.Equal(TimeSpan.Zero, completedAt);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteJoinTimesOutAtItsVirtualDeadline(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var thread = ControlledThread.Create(() =>
            {
                ControlledThread.Sleep(Timeout.Infinite);
            });
            ControlledThread.Start(thread);

            var joined = useTimeSpan
                ? ControlledThread.Join(thread, TimeSpan.FromMilliseconds(100))
                : ControlledThread.Join(thread, 100);

            Assert.False(joined);
            Assert.Equal(TimeSpan.FromMilliseconds(100), coordinator.Loop.VirtualNow);
            Assert.Equal(0, coordinator.Loop.ReadyCount);
            Assert.Equal(1, coordinator.Loop.WaitingCount);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            Assert.False(coordinator.Loop.IsIdle);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiniteJoinOverloadsWaitWithoutRegisteringADeadline(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            TimeSpan? deadlineSeenByBody = TimeSpan.MinValue;
            var thread = ControlledThread.Create(
                () => deadlineSeenByBody = coordinator.Loop.NextDeadlineDue());
            ControlledThread.Start(thread);

            var joined = useTimeSpan
                ? ControlledThread.Join(thread, Timeout.InfiniteTimeSpan)
                : ControlledThread.Join(thread, Timeout.Infinite);

            Assert.True(joined);
            Assert.Null(deadlineSeenByBody);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false, 125)]
    [InlineData(true, 250)]
    public void PositiveSleepAdvancesToExactVirtualTimestamp(bool useTimeSpan, int milliseconds)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var before = coordinator.Loop.VirtualNow;

            if (useTimeSpan)
            {
                ControlledThread.Sleep(TimeSpan.FromMilliseconds(milliseconds));
            }
            else
            {
                ControlledThread.Sleep(milliseconds);
            }

            Assert.Equal(TimeSpan.Zero, before);
            Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void PositiveSleepRunsReadyWorkBeforeAdvancingToWakeDeadline()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var order = new List<string>();
            var timestamps = new List<TimeSpan>();
            coordinator.Loop.Schedule(() =>
            {
                order.Add("other-work");
                timestamps.Add(coordinator.Loop.VirtualNow);
            });

            ControlledThread.Sleep(100);
            order.Add("sleeper-resume");
            timestamps.Add(coordinator.Loop.VirtualNow);

            Assert.Equal(["other-work", "sleeper-resume"], order);
            Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(100) }, timestamps);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroSleepYieldsWithoutAdvancingVirtualTime(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var order = new List<string>();
            var timestamps = new List<TimeSpan>();
            coordinator.Loop.Schedule(() =>
            {
                order.Add("ready-work");
                timestamps.Add(coordinator.Loop.VirtualNow);
            });

            if (useTimeSpan)
            {
                ControlledThread.Sleep(TimeSpan.Zero);
            }
            else
            {
                ControlledThread.Sleep(0);
            }

            order.Add("sleep-return");
            timestamps.Add(coordinator.Loop.VirtualNow);

            Assert.Equal(["ready-work", "sleep-return"], order);
            Assert.All(timestamps, timestamp => Assert.Equal(TimeSpan.Zero, timestamp));
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void YieldRunsOneReadyOperationWithoutAdvancingVirtualTime()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var order = new List<string>();
            coordinator.Loop.Schedule(() => order.Add("ready-work"));

            var yieldedWithReadyWork = ControlledThread.Yield();
            order.Add("yield-return");
            var yieldedWithoutReadyWork = ControlledThread.Yield();

            Assert.True(yieldedWithReadyWork);
            Assert.False(yieldedWithoutReadyWork);
            Assert.Equal(["ready-work", "yield-return"], order);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void SleepIntegerRejectsValuesLessThanInfinite(int millisecondsTimeout)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ControlledThread.Sleep(millisecondsTimeout));

            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal("millisecondsTimeout", exception.ParamName);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(-2L)]
    [InlineData((long)int.MaxValue + 1)]
    public void SleepTimeSpanRejectsOutOfRangeValues(long milliseconds)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ControlledThread.Sleep(TimeSpan.FromMilliseconds(milliseconds)));

            Assert.IsType<ArgumentOutOfRangeException>(exception);
            Assert.Equal("timeout", exception.ParamName);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InfiniteSleepParksWithoutADeadlineAndAllowsOtherWorkToRun(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var sleeperEntered = false;
            var sleeperResumed = false;
            var followerRan = false;
            TimeSpan? followerTimestamp = null;
            var sleeper = ControlledThread.Create(() =>
            {
                sleeperEntered = true;
                if (useTimeSpan)
                {
                    ControlledThread.Sleep(Timeout.InfiniteTimeSpan);
                }
                else
                {
                    ControlledThread.Sleep(Timeout.Infinite);
                }

                sleeperResumed = true;
            });
            var follower = ControlledThread.Create(() =>
            {
                followerRan = true;
                followerTimestamp = coordinator.Loop.VirtualNow;
            });

            ControlledThread.Start(sleeper);
            ControlledThread.Start(follower);
            coordinator.Loop.RunUntilIdle();

            Assert.True(sleeperEntered);
            Assert.False(sleeperResumed);
            Assert.True(followerRan);
            Assert.Equal(TimeSpan.Zero, followerTimestamp);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            Assert.Equal(0, coordinator.Loop.ReadyCount);
            Assert.Equal(1, coordinator.Loop.WaitingCount);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            Assert.False(coordinator.Loop.IsIdle);
        });
    }

    [Fact]
    public void ThreadStartRejectsStartWithParameter()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var runs = 0;
            var thread = ControlledThread.Create((ThreadStart)(() => runs++));

            var exception = Assert.Throws<InvalidOperationException>(
                () => ControlledThread.Start(thread, new object()));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal(0, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);

            ControlledThread.Start(thread);
            Assert.Equal(1, coordinator.Loop.ReadyCount);
            ControlledThread.Join(thread);

            Assert.Equal(1, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    private static void AssertLoopIsEmpty(ControlledTaskLoopCoordinator coordinator)
    {
        Assert.Equal(0, coordinator.Loop.ReadyCount);
        Assert.Equal(0, coordinator.Loop.WaitingCount);
        Assert.Null(coordinator.Loop.NextDeadlineDue());
        Assert.True(coordinator.Loop.IsIdle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteJoinWaitsForDelayedCompletionBeforeItsDeadline(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            TimeSpan? completedAt = null;
            var thread = ControlledThread.Create(() =>
            {
                ControlledThread.Sleep(50);
                completedAt = coordinator.Loop.VirtualNow;
            });
            ControlledThread.Start(thread);

            var joined = useTimeSpan
                ? ControlledThread.Join(thread, TimeSpan.FromMilliseconds(100))
                : ControlledThread.Join(thread, 100);

            Assert.True(joined);
            Assert.Equal(TimeSpan.FromMilliseconds(50), completedAt);
            Assert.Equal(TimeSpan.FromMilliseconds(50), coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothZeroJoinOverloadsLeavePendingBodyQueued(bool useTimeSpan)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var runs = 0;
            var thread = ControlledThread.Create(() => runs++);
            ControlledThread.Start(thread);

            var joined = useTimeSpan
                ? ControlledThread.Join(thread, TimeSpan.Zero)
                : ControlledThread.Join(thread, 0);

            Assert.False(joined);
            Assert.Equal(0, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            Assert.Equal(1, coordinator.Loop.ReadyCount);
            Assert.Equal(0, coordinator.Loop.WaitingCount);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            Assert.False(coordinator.Loop.IsIdle);

            Assert.Equal(1, coordinator.Loop.RunUntilIdle());
            Assert.Equal(1, runs);
            Assert.True(
                useTimeSpan
                    ? ControlledThread.Join(thread, TimeSpan.Zero)
                    : ControlledThread.Join(thread, 0));
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void JoinTimeSpanAcceptsExactMaximumMillisecondsBoundary()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            TimeSpan? completedAt = null;
            var thread = ControlledThread.Create(() =>
            {
                ControlledThread.Sleep(25);
                completedAt = coordinator.Loop.VirtualNow;
            });
            ControlledThread.Start(thread);

            var joined = ControlledThread.Join(
                thread,
                TimeSpan.FromMilliseconds(int.MaxValue));

            Assert.True(joined);
            Assert.Equal(TimeSpan.FromMilliseconds(25), completedAt);
            Assert.Equal(TimeSpan.FromMilliseconds(25), coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void SleepTimeSpanAcceptsExactMaximumMillisecondsBoundary()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            TimeSpan? completedAt = null;
            var thread = ControlledThread.Create(() =>
            {
                ControlledThread.Sleep(TimeSpan.FromMilliseconds(int.MaxValue));
                completedAt = coordinator.Loop.VirtualNow;
            });
            ControlledThread.Start(thread);
            ControlledThread.Join(thread);

            var expected = TimeSpan.FromMilliseconds(int.MaxValue);
            Assert.Equal(expected, completedAt);
            Assert.Equal(expected, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Fact]
    public void YieldExecutesExactlyOneOfTwoReadyOperations()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var order = new List<string>();
            coordinator.Loop.Schedule(() => order.Add("first"));
            coordinator.Loop.Schedule(() => order.Add("second"));

            var yielded = ControlledThread.Yield();

            Assert.True(yielded);
            Assert.Equal(["first"], order);
            Assert.Equal(1, coordinator.Loop.ReadyCount);
            Assert.Equal(0, coordinator.Loop.WaitingCount);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            Assert.False(coordinator.Loop.IsIdle);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);

            Assert.Equal(1, coordinator.Loop.RunUntilIdle());
            Assert.Equal(["first", "second"], order);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreadStartRejectsNullAndNonNullParametersWithoutConsumingStart(bool useNull)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var runs = 0;
            var thread = ControlledThread.Create((ThreadStart)(() => runs++));
            object? parameter = useNull ? null : new object();

            var exception = Assert.Throws<InvalidOperationException>(
                () => ControlledThread.Start(thread, parameter));

            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal(0, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);

            ControlledThread.Start(thread);
            Assert.Equal(1, coordinator.Loop.ReadyCount);
            ControlledThread.Join(thread);

            Assert.Equal(1, runs);
            Assert.Equal(TimeSpan.Zero, coordinator.Loop.VirtualNow);
            AssertLoopIsEmpty(coordinator);
        });
    }
}
