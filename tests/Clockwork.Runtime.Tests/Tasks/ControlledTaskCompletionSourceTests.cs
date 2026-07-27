using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Tasks.CompilerServices;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Tests for <see cref="ControlledTaskCompletionSource"/> and its generic variant: success/fault/cancel
/// transitions, the try-variants, and the key property that
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is neutralized inside a simulation so
/// completion stays on the logical thread while awaited continuations still run through the coordinator.
/// </summary>
public sealed class ControlledTaskCompletionSourceTests
{
    [Fact]
    public void SetResultCompletesTheTaskOnTheLogicalThread()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource<int>();
            Assert.False(tcs.Task.IsCompleted);

            tcs.SetResult(42);

            Assert.True(tcs.Task.IsCompletedSuccessfully);
            Assert.Equal(42, tcs.Task.Result);
        });
    }

    [Fact]
    public void NonGenericSetResultCompletesTheTask()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource();
            tcs.SetResult();
            Assert.True(tcs.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void SetExceptionFaultsTheTask()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource<int>();
            tcs.SetException(new InvalidOperationException("boom"));

            Assert.True(tcs.Task.IsFaulted);
            Assert.IsType<InvalidOperationException>(tcs.Task.Exception!.InnerException);
        });
    }

    [Fact]
    public void SetCanceledCancelsTheTask()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource<int>();
            tcs.SetCanceled();
            Assert.True(tcs.Task.IsCanceled);
        });
    }

    [Fact]
    public void TryVariantsReturnFalseOnSecondTransition()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource<int>();
            Assert.True(tcs.TrySetResult(1));
            Assert.False(tcs.TrySetResult(2));
            Assert.False(tcs.TrySetException(new InvalidOperationException()));
            Assert.False(tcs.TrySetCanceled());
            Assert.Equal(1, tcs.Task.Result);
        });
    }

    [Fact]
    public void RunContinuationsAsynchronouslyIsNeutralizedInsideSimulation()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // Even when asked to run continuations asynchronously, completion must happen synchronously on
            // the logical thread inside a simulation (the flag is dropped). If the flag leaked through, the
            // task would be posted to a real scheduler and would not be observably complete right here.
            var tcs = new ControlledTaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.SetResult();
            Assert.True(tcs.Task.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task OptionsAreHonoredOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        var tcs = new ControlledTaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(7);
        Assert.Equal(7, await tcs.Task);
    }

    [Fact]
    public void AwaitedCompletionSourceResumesThroughTheCoordinator()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new ControlledTaskCompletionSource<int>();
            var observed = 0;
            var completed = false;

            // Await the controlled TCS's task via a controlled awaiter; the continuation must be scheduled
            // through the loop, not run inline, and must observe the result after completion.
            var awaiter = new ControlledTaskAwaiter<int>(tcs.Task);
            awaiter.OnCompleted(() =>
            {
                observed = awaiter.GetResult();
                completed = true;
            });

            Assert.False(completed);

            coordinator.Loop.Schedule(() => tcs.SetResult(99));
            coordinator.Loop.RunUntil(() => completed, "test");

            Assert.Equal(99, observed);
        });
    }
}
