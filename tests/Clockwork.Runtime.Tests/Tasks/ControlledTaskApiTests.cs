using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Threading;

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

#pragma warning disable xUnit1051 // The token is part of the overload under test; no asynchronous wait occurs.
    public static TheoryData<Func<Task>> DelayCalls =>
        new()
        {
            () => ControlledTask.Delay(100),
            () => ControlledTask.Delay(TimeSpan.FromMilliseconds(100)),
            () => ControlledTask.Delay(100, CancellationToken.None),
            () => ControlledTask.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None),
            () => ControlledTask.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System),
            () => ControlledTask.Delay(TimeSpan.FromMilliseconds(100), TimeProvider.System, CancellationToken.None),
        };
#pragma warning restore xUnit1051

    [Theory]
    [MemberData(nameof(DelayCalls))]
    public void EveryDelayOverloadIsRejectedInsideSimulation(Func<Task> delay)
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var ex = Assert.Throws<ControlledTaskUnsupportedException>(() => { _ = delay(); });
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
#pragma warning disable xUnit1051 // Each exact overload, including those without tokens, is the subject under test.
    public async Task DelayAndRunPassThroughOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        // Outside a simulation these must behave exactly like the real BCL APIs.
        Task[] delays =
        [
            ControlledTask.Delay(0),
            ControlledTask.Delay(TimeSpan.Zero),
            ControlledTask.Delay(0, TestContext.Current.CancellationToken),
            ControlledTask.Delay(TimeSpan.Zero, TestContext.Current.CancellationToken),
            ControlledTask.Delay(TimeSpan.Zero, TimeProvider.System),
            ControlledTask.Delay(TimeSpan.Zero, TimeProvider.System, TestContext.Current.CancellationToken),
        ];
        var run = ControlledTask.Run(() => { }, TestContext.Current.CancellationToken);
        await Task.WhenAll([.. delays, run]);

        Assert.All(delays, delay => Assert.True(delay.IsCompletedSuccessfully));
        Assert.True(run.IsCompletedSuccessfully);
    }
#pragma warning restore xUnit1051

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
    public void TaskFactoryStateAndFullSchedulerFormsRunOnControlledStrands()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            long actionStrand = ControlledSynchronizationFlow.None;
            long actionState = 0;
            var action = ControlledTaskFactory.StartNew(
                Task.Factory,
                state =>
                {
                    actionState = (long)state!;
                    actionStrand = ControlledSynchronizationFlow.CurrentId;
                },
                17L,
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);
            var result = ControlledTaskFactory.StartNew(
                Task.Factory,
                state => (Value: (int)state!, Strand: ControlledSynchronizationFlow.CurrentId),
                42,
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);

            coordinator.Loop.RunUntil(() => action.IsCompleted && result.IsCompleted, "test");

            Assert.Equal(17L, actionState);
            Assert.Equal(17L, action.AsyncState);
            Assert.NotEqual(ControlledSynchronizationFlow.None, actionStrand);
            Assert.Equal(42, result.Result.Value);
            Assert.Equal(42, result.AsyncState);
            Assert.NotEqual(ControlledSynchronizationFlow.None, result.Result.Strand);
            Assert.True(action.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void GenericTaskFactoryStateFormPreservesStateAndResult()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var factory = new TaskFactory<int>();
            var result = ControlledTaskFactory.StartNew(
                factory,
                state => (int)state! * 2,
                21,
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);

            coordinator.Loop.RunUntil(() => result.IsCompleted, "test");
            Assert.Equal(42, result.Result);
            Assert.Equal(21, result.AsyncState);
        });
    }

    [Fact]
    public void TaskFactoryRejectsUnsupportedSchedulerAndOptions()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var schedulers = new ConcurrentExclusiveSchedulerPair();
            Assert.Throws<ControlledTaskUnsupportedException>((Action)(() =>
            {
                _ = ControlledTaskFactory.StartNew(
                    Task.Factory,
                    () => { },
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    schedulers.ExclusiveScheduler);
            }));
            Assert.Throws<ControlledTaskUnsupportedException>((Action)(() =>
            {
                _ = ControlledTaskFactory.StartNew(
                    Task.Factory,
                    () => { },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }));
            Assert.Throws<ControlledTaskUnsupportedException>((Action)(() =>
            {
                _ = ControlledTaskFactory.StartNew(
                    Task.Factory,
                    () => { },
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }));
            schedulers.Complete();
        });
    }

    [Fact]
    public void TaskFactoryPreCancellationIsImmediateAndUnmatchedCancellationFaults()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ran = false;
            Task canceled = ControlledTaskFactory.StartNew(
                Task.Factory,
                () => ran = true,
                cts.Token,
                TaskCreationOptions.None,
                TaskScheduler.Default);
            Task faulted = ControlledTaskFactory.StartNew(
                Task.Factory,
                () => throw new OperationCanceledException(),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);

            Assert.True(canceled.IsCanceled);
            Assert.False(ran);
            coordinator.Loop.RunUntil(() => faulted.IsCompleted, "test");
            Assert.Equal(TaskStatus.Faulted, faulted.Status);
            Assert.IsType<OperationCanceledException>(faulted.Exception!.InnerException);
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
                // Exercise the options-only overload independently from the full scheduler form.
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

    [Fact]
    public async Task TaskFactoryStateAndCustomSchedulerPassThroughOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);
        var schedulers = new ConcurrentExclusiveSchedulerPair();
        var task = ControlledTaskFactory.StartNew(
            Task.Factory,
            state => (int)state! * 2,
            6,
            TestContext.Current.CancellationToken,
            TaskCreationOptions.None,
            schedulers.ExclusiveScheduler);

        Assert.Equal(12, await task);
        schedulers.Complete();
        await schedulers.Completion;
    }
}
