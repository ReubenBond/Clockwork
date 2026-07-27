using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Tests for the <see cref="ControlledTask"/> static shims: combinator determinism, synchronous waits
/// that pump instead of block (and surface deadlock), continuations routed through the loop, and the
/// explicit rejection of timer/thread-pool APIs deferred to later phases.
/// </summary>
public sealed class ControlledTaskApiTests
{
    [Fact]
    public void WhenAllCompletesAfterAllAntecedents()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var a = new TaskCompletionSource<int>();
            var b = new TaskCompletionSource<int>();
            var all = ControlledTask.WhenAll(a.Task, b.Task);

            Assert.False(all.IsCompleted);

            coordinator.Loop.Schedule(() => a.SetResult(1));
            coordinator.Loop.Schedule(() => b.SetResult(2));
            coordinator.Loop.RunUntil(() => all.IsCompleted, "test");

            Assert.Equal([1, 2], all.Result);
        });
    }

    [Fact]
    public void WhenAnyReportsTheDeterministicFirstCompleter()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var a = new TaskCompletionSource<int>();
            var b = new TaskCompletionSource<int>();
            var any = ControlledTask.WhenAny(a.Task, b.Task);

            // b is completed first on the loop, so WhenAny must resolve to b, deterministically.
            coordinator.Loop.Schedule(() => b.SetResult(2));
            coordinator.Loop.Schedule(() => a.SetResult(1));
            coordinator.Loop.RunUntil(() => any.IsCompleted, "test");

            Assert.Same(b.Task, any.Result);
        });
    }

    [Fact]
    public void WaitPumpsUntilCompletionWithoutBlocking()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource();
            coordinator.Loop.Schedule(() => tcs.SetResult());

            ControlledTask.Wait(tcs.Task);

            Assert.True(tcs.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void WaitThrowsAggregateExceptionOnFault()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource();
            var boom = new InvalidTimeZoneException("boom");
            coordinator.Loop.Schedule(() => tcs.SetException(boom));

            var ex = Assert.Throws<AggregateException>(() => ControlledTask.Wait(tcs.Task));
            Assert.Same(boom, ex.InnerException);
        });
    }

    [Fact]
    public void ResultPumpsUntilCompletion()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource<int>();
            coordinator.Loop.Schedule(() => tcs.SetResult(42));

            var value = ControlledTask.Result(tcs.Task);

            Assert.Equal(42, value);
        });
    }

    [Fact]
    public void WaitOnANeverCompletingTaskSurfacesDeadlock()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource();
            Assert.Throws<ControlledSynchronousWaitDeadlockException>(() => ControlledTask.Wait(tcs.Task));
        });
    }

    [Fact]
    public void WaitAllPumpsUntilAllComplete()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var a = new TaskCompletionSource();
            var b = new TaskCompletionSource();
            coordinator.Loop.Schedule(() => a.SetResult());
            coordinator.Loop.Schedule(() => b.SetResult());

            ControlledTask.WaitAll(a.Task, b.Task);

            Assert.True(a.Task.IsCompleted && b.Task.IsCompleted);
        });
    }

    [Fact]
    public void WaitAnyReturnsIndexOfFirstCompleted()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var a = new TaskCompletionSource();
            var b = new TaskCompletionSource();
            coordinator.Loop.Schedule(() => b.SetResult());

            var index = ControlledTask.WaitAny(a.Task, b.Task);

            Assert.Equal(1, index);
        });
    }

    [Fact]
    public void ContinueWithRunsAfterAntecedentThroughTheLoop()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource<int>();
            var ranInline = false;
            var continuation = ControlledTask.ContinueWith(tcs.Task, t => ranInline = false);

            coordinator.Loop.Schedule(() =>
            {
                tcs.SetResult(5);
                ranInline = continuation.IsCompleted;
            });
            coordinator.Loop.RunUntil(() => continuation.IsCompleted, "test");

            Assert.False(ranInline);
            Assert.True(continuation.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void ContinueWithFuncProducesResult()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource<int>();
            var continuation = ControlledTask.ContinueWith(tcs.Task, t => ((Task<int>)t).Result * 2);

            coordinator.Loop.Schedule(() => tcs.SetResult(21));
            coordinator.Loop.RunUntil(() => continuation.IsCompleted, "test");

            Assert.Equal(42, continuation.Result);
        });
    }

    [Fact]
    public void ContinueWithRunsEvenWhenAntecedentFaults()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource<int>();
            var observedFault = false;
            var continuation = ControlledTask.ContinueWith(tcs.Task, t => observedFault = t.IsFaulted);

            coordinator.Loop.Schedule(() => tcs.SetException(new InvalidTimeZoneException()));
            coordinator.Loop.RunUntil(() => continuation.IsCompleted, "test");

            Assert.True(observedFault);
            Assert.True(continuation.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void DelayIsRejectedInsideSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledTaskUnsupportedException>(() => { _ = ControlledTask.Delay(100); });
            Assert.Equal("System.Threading.Tasks.Task.Delay", ex.ApiName);
        });
    }

    [Fact]
    public void RunIsRejectedInsideSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledTaskUnsupportedException>(() => { _ = ControlledTask.Run(() => { }); });
            Assert.Equal("System.Threading.Tasks.Task.Run", ex.ApiName);
        });
    }

    [Fact]
    public async Task DelayAndRunPassThroughOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        // Outside a simulation these must behave exactly like the real BCL APIs.
        var delay = ControlledTask.Delay(1);
        var run = ControlledTask.Run(() => { });
        await Task.WhenAll(delay, run);

        Assert.True(delay.IsCompletedSuccessfully);
        Assert.True(run.IsCompletedSuccessfully);
    }
}
