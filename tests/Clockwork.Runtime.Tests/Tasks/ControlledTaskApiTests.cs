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
    public void RunQueuesBodyAsControlledWorkAndCompletes()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ran = false;
            var task = ControlledTask.Run(() => { ran = true; });

            // The body is queued, not run inline: nothing has executed until the loop is pumped.
            Assert.False(ran);
            Assert.False(task.IsCompleted);

            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");

            Assert.True(ran);
            Assert.True(task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void RunOfFuncReturnsResultDeterministically()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var task = ControlledTask.Run(() => 42);
            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");
            Assert.Equal(42, task.Result);
        });
    }

    [Fact]
    public void RunOfAsyncFuncUnwrapsInnerTask()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var inner = new TaskCompletionSource<int>();
            var task = ControlledTask.Run(() => inner.Task);

            // The outer task must not complete until the unwrapped inner task completes.
            coordinator.Loop.RunUntilIdle();
            Assert.False(task.IsCompleted);

            coordinator.Loop.Schedule(() => inner.SetResult(7));
            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");

            Assert.Equal(7, task.Result);
        });
    }

    [Fact]
    public void RunPropagatesTheBodyFault()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var boom = new InvalidTimeZoneException("boom");
            var task = ControlledTask.Run(() => throw boom);
            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");

            Assert.Equal(TaskStatus.Faulted, task.Status);
            Assert.Same(boom, task.Exception!.InnerException);
        });
    }

    [Fact]
    public void RunWithAlreadyCanceledTokenCancelsAndDoesNotRunBody()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            var ran = false;
            var task = ControlledTask.Run(() => { ran = true; }, cts.Token);
            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");

            Assert.False(ran);
            Assert.Equal(TaskStatus.Canceled, task.Status);
        });
    }

    [Fact]
    public async Task DelayAndRunPassThroughOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        // Outside a simulation these must behave exactly like the real BCL APIs.
        var delay = ControlledTask.Delay(1);
        var run = ControlledTask.Run(() => { }, TestContext.Current.CancellationToken);
        await Task.WhenAll(delay, run);

        Assert.True(delay.IsCompletedSuccessfully);
        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public void TaskFactoryStartNewQueuesBodyAsControlledWork()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ran = false;
            var task = ControlledTaskFactory.StartNew(Task.Factory, () => { ran = true; });

            // The body is queued, not run inline.
            Assert.False(ran);
            Assert.False(task.IsCompleted);

            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");

            Assert.True(ran);
            Assert.True(task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void TaskFactoryStartNewOfFuncReturnsResult()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var task = ControlledTaskFactory.StartNew(Task.Factory, () => 99);
            coordinator.Loop.RunUntil(() => task.IsCompleted, "test");
            Assert.Equal(99, task.Result);
        });
    }

    [Fact]
    public void TaskFactoryStartNewRejectsAttachedToParent()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledTaskUnsupportedException>(() =>
            {
                // Intentionally the TaskCreationOptions overload (no CancellationToken variant exists for it).
#pragma warning disable xUnit1051
                _ = ControlledTaskFactory.StartNew(Task.Factory, () => { }, TaskCreationOptions.AttachedToParent);
#pragma warning restore xUnit1051
            });
            Assert.Contains("AttachedToParent", ex.Message);
        });
    }

    [Fact]
    public void GenericContinueWithProjectsAntecedentResult()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new TaskCompletionSource<int>();
            var projected = ControlledTask.ContinueWith(source.Task, t => t.Result.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Assert.False(projected.IsCompleted);

            coordinator.Loop.Schedule(() => source.SetResult(7));
            coordinator.Loop.RunUntil(() => projected.IsCompleted, "test");

            Assert.Equal("7", projected.Result);
        });
    }

    [Fact]
    public void GenericContinueWithActionObservesTypedAntecedent()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new TaskCompletionSource<int>();
            var seen = 0;
            var continuation = ControlledTask.ContinueWith(source.Task, t => { seen = t.Result; });

            coordinator.Loop.Schedule(() => source.SetResult(11));
            coordinator.Loop.RunUntil(() => continuation.IsCompleted, "test");

            Assert.Equal(11, seen);
            Assert.True(continuation.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task TaskFactoryStartNewPassesThroughOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        var task = ControlledTaskFactory.StartNew(Task.Factory, () => 5);
        var value = await task;

        Assert.Equal(5, value);
    }
}
