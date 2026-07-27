using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tests.Shims;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tests.Threading;

public sealed class ControlledTimerFamilyTests
{
    [Fact]
    public void TimersTimerOneShotRaisesElapsedAtVirtualTime()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var elapsed = new List<System.Timers.ElapsedEventArgs>();
        var runtime = TaskTestHarness.NewRuntime();
        var clock = ShimTestHarness.CreateClock();
        using var environment = SimulationRuntimeServices.Register(
            SimulationRuntimeActivation.CreateToken(),
            runtime,
            ShimTestHarness.CreateEnvironment(clock));

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledTimersTimer(TimeSpan.FromMilliseconds(25))
            {
                AutoReset = false,
            };
            timer.Elapsed += (_, args) => elapsed.Add(args);
            timer.Start();

            clock.Advance(TimeSpan.FromMilliseconds(25));
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(25));
            coordinator.Loop.RunUntilIdle();

            Assert.False(timer.Enabled);
            Assert.Single(elapsed);
            Assert.Null(coordinator.Loop.NextDeadlineDue());
        }, runtime: runtime);
    }

    [Fact]
    public void TimersTimerStopAndCloseCancelPendingElapsedEvents()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var count = 0;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledTimersTimer(10);
            timer.Elapsed += (_, _) => count++;
            timer.Start();
            timer.Stop();
            Assert.Null(coordinator.Loop.NextDeadlineDue());

            timer.Start();
            timer.Close();
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(100));
            coordinator.Loop.RunUntilIdle();
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public void TimersTimerRejectsUncontrolledMarshaling()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledTimersTimer();
            Assert.Throws<ControlledTimerUnsupportedException>(
                () => timer.SynchronizingObject = new SynchronizingObjectStub());
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    public void TimersTimerConstructorValidatesInterval(double interval)
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(
            coordinator,
            () => Assert.Throws<ArgumentException>(() => new ControlledTimersTimer(interval)));
    }

    [Fact]
    public void PeriodicTimerCoalescesTicksUntilConsumed()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledPeriodicTimer(TimeSpan.FromMilliseconds(10));

            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(10));
            coordinator.Loop.RunUntilIdle();
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(20));
            coordinator.Loop.RunUntilIdle();

            ValueTask<bool> tick = timer.WaitForNextTickAsync();
            Assert.True(tick.IsCompletedSuccessfully);
            Assert.True(tick.Result);

            ValueTask<bool> next = timer.WaitForNextTickAsync();
            Assert.False(next.IsCompleted);
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(30));
            coordinator.Loop.RunUntilIdle();
            Assert.True(next.Result);
        });
    }

    [Fact]
    public void PeriodicTimerEnforcesSingleConsumerAndDisposeReturnsFalse()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var timer = new ControlledPeriodicTimer(TimeSpan.FromMilliseconds(10));
            ValueTask<bool> waiter = timer.WaitForNextTickAsync();
            Assert.Throws<InvalidOperationException>(
                () => GC.KeepAlive(timer.WaitForNextTickAsync(TestContext.Current.CancellationToken).AsTask()));

            timer.Dispose();
            Assert.True(waiter.IsCompleted);
            Assert.False(waiter.Result);
            ValueTask<bool> afterDispose = timer.WaitForNextTickAsync();
            Assert.True(afterDispose.IsCompletedSuccessfully);
            Assert.False(afterDispose.Result);
        });
    }

    [Fact]
    public void PeriodicTimerCancellationOnlyCancelsTheCurrentWait()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledPeriodicTimer(TimeSpan.FromMilliseconds(10));
            using var cancellation = new CancellationTokenSource();
            ValueTask<bool> waiter = timer.WaitForNextTickAsync(cancellation.Token);
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => waiter.GetAwaiter().GetResult());

            ValueTask<bool> next = timer.WaitForNextTickAsync();
            coordinator.Loop.AdvanceTimeTo(TimeSpan.FromMilliseconds(10));
            coordinator.Loop.RunUntilIdle();
            Assert.True(next.Result);
        });
    }

    [Fact]
    public void PeriodicTimerPeriodChangeResetsTheFutureSchedule()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var timer = new ControlledPeriodicTimer(TimeSpan.FromMilliseconds(10));
            timer.Period = TimeSpan.FromMilliseconds(25);
            Assert.Equal(TimeSpan.FromMilliseconds(25), timer.Period);
            Assert.Equal(TimeSpan.FromMilliseconds(25), coordinator.Loop.NextDeadlineDue());
        });
    }

    private sealed class SynchronizingObjectStub : System.ComponentModel.ISynchronizeInvoke
    {
        public bool InvokeRequired => true;

        public IAsyncResult BeginInvoke(Delegate method, object?[]? args) => throw new NotSupportedException();

        public object? EndInvoke(IAsyncResult result) => throw new NotSupportedException();

        public object? Invoke(Delegate method, object?[]? args) => throw new NotSupportedException();
    }
}
