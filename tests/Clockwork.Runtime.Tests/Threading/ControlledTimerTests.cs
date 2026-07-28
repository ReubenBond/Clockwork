using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

public sealed class ControlledTimerTests
{
    [Fact]
    public void PublicEntriesRequireActiveSimulation()
    {
        Assert.Throws<Clockwork.Runtime.Shims.SimulationNotActiveException>(() => new ControlledTimer(_ => { }));
        Assert.Throws<Clockwork.Runtime.Shims.SimulationNotActiveException>(() => _ = ControlledTimer.ActiveCount);
    }

    [Fact]
    public void ConstructorOnlyTimerIsDisabledAndUsesItselfAsState()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        object? observed = null;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledTimer(state => observed = state);
            Assert.Equal(0, ControlledTimer.ActiveCount);
            Assert.True(timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
            coordinator.Loop.RunUntilIdle();
            Assert.Same(timer, observed);
            Assert.Equal(0, ControlledTimer.ActiveCount);
        });
    }

    [Fact]
    public void OneShotAndPeriodicFiringsUseVirtualTime()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var fired = new List<TimeSpan>();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledTimer(
                _ => fired.Add(coordinator.Loop.VirtualNow),
                null,
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(20));

            Assert.Equal(TimeSpan.FromMilliseconds(10), coordinator.Loop.NextDeadlineDue());
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(10));
            coordinator.Loop.RunUntilIdle();
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(30));
            coordinator.Loop.RunUntilIdle();

            Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30)], fired);
            Assert.Equal(TimeSpan.FromMilliseconds(50), coordinator.Loop.NextDeadlineDue());
        });
    }

    [Fact]
    public void ReentrantChangeCancelsTheOldPeriodicGeneration()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        ControlledTimer? timer = null;
        var fired = new List<TimeSpan>();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            timer = new ControlledTimer(
                _ =>
                {
                    fired.Add(coordinator.Loop.VirtualNow);
                    if (fired.Count == 1)
                    {
                        Assert.True(timer!.Change(5, Timeout.Infinite));
                    }
                },
                null,
                1,
                2);

            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(1));
            coordinator.Loop.RunUntilIdle();
            Assert.Equal(TimeSpan.FromMilliseconds(6), coordinator.Loop.NextDeadlineDue());

            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(6));
            coordinator.Loop.RunUntilIdle();

            Assert.Equal([TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(6)], fired);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
            timer!.Dispose();
        });
    }

    [Fact]
    public void DisposeSuppressesAnAlreadyQueuedCallback()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var fired = false;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var timer = new ControlledTimer(_ => fired = true, null, 0, Timeout.Infinite);
            timer.Dispose();
            coordinator.Loop.RunUntilIdle();
        });

        Assert.False(fired);
    }

    [Fact]
    public void CallbackFlowsConstructionExecutionContextOnAFreshStrand()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var local = new AsyncLocal<string?>();
        string? observed = null;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            local.Value = "captured";
            using var timer = new ControlledTimer(_ => observed = local.Value, null, 0, Timeout.Infinite);
            local.Value = "changed";
            coordinator.Loop.RunUntilIdle();
        });

        Assert.Equal("captured", observed);
    }

    [Fact]
    public void SameDeadlineCallbacksRunInRegistrationOrder()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var order = new List<int>();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var first = new ControlledTimer(_ => order.Add(1), null, 5, Timeout.Infinite);
            using var second = new ControlledTimer(_ => order.Add(2), null, 5, Timeout.Infinite);
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(5));
            coordinator.Loop.RunUntilIdle();
        });

        Assert.Equal([1, 2], order);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void SignedTimeoutValidationMatchesTheBcl(int invalid)
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ControlledTimer(_ => { }, null, invalid, Timeout.Infinite));
            using var timer = new ControlledTimer(_ => { });
            Assert.Throws<ArgumentOutOfRangeException>(() => timer.Change(invalid, Timeout.Infinite));
        });
    }

    [Fact]
    public void DisposeWaitHandleSignalsControlledEventAndRejectsKernelEscape()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var timer = new ControlledTimer(_ => { });
            using ManualResetEvent completion = ControlledEventWaitHandle.CreateManualResetEvent(false);

            Assert.True(timer.Dispose(completion));
            Assert.True(ControlledWaitHandle.WaitOne(completion, 0));

            using var uncontrolled = new ManualResetEvent(false);
            using var second = new ControlledTimer(_ => { });
            Assert.Throws<ControlledWaitHandleUnsupportedException>(() => second.Dispose(uncontrolled));
        });
    }
}
