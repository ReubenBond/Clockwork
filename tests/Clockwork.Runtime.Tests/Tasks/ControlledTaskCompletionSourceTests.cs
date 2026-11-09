using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;
using Clockwork.Shims.System.Runtime.CompilerServices;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Tests for the <see cref="ControlledTaskCompletionSource"/> completion redirects: each completes the real BCL
/// completion source through the same member, so the result, the try-variants, and the source's creation options are
/// the BCL's own, and an awaited completion still resumes through the coordinator rather than inside the completion.
/// </summary>
public sealed class ControlledTaskCompletionSourceTests
{
    [Fact]
    public void CompletionMembersCompleteTheRealSource()
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var result = new TaskCompletionSource<int>();
            ControlledTaskCompletionSource.SetResult(result, 42);
            Assert.Equal(42, result.Task.Result);

            var fault = new TaskCompletionSource();
            var boom = new InvalidOperationException("boom");
            ControlledTaskCompletionSource.SetException(fault, boom);
            Assert.Same(boom, fault.Task.Exception!.InnerException);

            var token = new CancellationToken(canceled: true);
            var canceled = new TaskCompletionSource<int>();
            ControlledTaskCompletionSource.SetCanceled(canceled, token);
            Assert.Equal(token, Assert.ThrowsAny<OperationCanceledException>(() => canceled.Task.GetAwaiter().GetResult()).CancellationToken);

            var copied = new TaskCompletionSource<int>();
            ControlledTaskCompletionSource.SetFromTask(copied, Task.FromResult(7));
            Assert.Equal(7, copied.Task.Result);
        });
    }

    [Fact]
    public void TryVariantsReturnTheBclOutcome()
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new TaskCompletionSource<int>();
            Assert.True(ControlledTaskCompletionSource.TrySetResult(source, 1));
            Assert.False(ControlledTaskCompletionSource.TrySetResult(source, 2));
            Assert.False(ControlledTaskCompletionSource.TrySetException(source, new InvalidOperationException()));
            Assert.False(ControlledTaskCompletionSource.TrySetCanceled(source));
            Assert.False(ControlledTaskCompletionSource.TrySetFromTask(source, Task.FromResult(3)));
            Assert.Equal(1, source.Task.Result);
            Assert.Throws<InvalidOperationException>(() => ControlledTaskCompletionSource.SetResult(source, 4));
            Assert.Throws<ArgumentException>(
                () => ControlledTaskCompletionSource.TrySetFromTask(new TaskCompletionSource(), new TaskCompletionSource().Task));
        });
    }

    [Fact]
    public void CreationOptionsAreTheSourcesOwn()
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ControlledTaskCompletionSource.SetResult(source);

            Assert.True(source.Task.IsCompletedSuccessfully);
            Assert.Equal(TaskCreationOptions.RunContinuationsAsynchronously, source.Task.CreationOptions);
        });
    }

    [Fact]
    public void NullSourceThrowsTheRuntimeNullReferenceException()
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<NullReferenceException>(() => ControlledTaskCompletionSource.SetResult(null!));
            Assert.Throws<NullReferenceException>(() => ControlledTaskCompletionSource.TrySetResult<int>(null!, 1));
        });
    }

    [Fact]
    public void CompletionOutsideSimulationRequiresActiveSimulation()
    {
        Assert.False(SimulationTaskRuntime.IsSimulationActive);
        var source = new TaskCompletionSource<int>();

        Exception? exception = Record.Exception(() => ControlledTaskCompletionSource.SetResult(source, 1));

        Assert.False(source.Task.IsCompleted);
        SimulationNotActiveExceptionAssert.Equal(exception, "System.Threading.Tasks.TaskCompletionSource`1");
    }

    [Theory]
    [InlineData(TaskCreationOptions.None)]
    [InlineData(TaskCreationOptions.RunContinuationsAsynchronously)]
    public void AwaitedCompletionResumesThroughTheCoordinatorAfterCompletion(TaskCreationOptions options)
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var tcs = new TaskCompletionSource<int>(options);
            var log = new List<string>();
            var awaiter = new ControlledTaskAwaiter<int>(tcs.Task);
            awaiter.OnCompleted(() => log.Add("resumed " + awaiter.GetResult()));

            coordinator.Scheduler.Schedule(() =>
            {
                log.Add("before");
                ControlledTaskCompletionSource.SetResult(tcs, 99);
                log.Add("after");
            });
            coordinator.Scheduler.DrainUntil(() => log.Count == 3, "test", TestContext.Current.CancellationToken);

            Assert.Equal(["before", "after", "resumed 99"], log);
        });
    }
}
