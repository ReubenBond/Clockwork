using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Covers coordinator access through a complete ambient runtime plus continuation and synchronous-wait
/// routing through that coordinator.
/// </summary>
public sealed class ControlledTaskRuntimeTests
{
    [Fact]
    public void RequireSchedulerOutsideSimulationRequiresActiveSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);

        Exception? exception = Record.Exception(
            () => ControlledTaskRuntime.RequireScheduler("test.api"));

        SimulationNotActiveExceptionAssert.Equal(exception, "test.api");
    }

    [Fact]
    public void RequireSchedulerReturnsNodeWhenActiveAndRegistered()
    {
        var coordinator = new SimulationSchedulerTestHost();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var (resolved, node) = ControlledTaskRuntime.RequireScheduler("test.api");
            Assert.Same(coordinator.Scheduler, resolved);
            Assert.NotNull(node);
            Assert.Equal(TaskTestHarness.DefaultNodeAddress, node!.Address);
        });
    }

    [Fact]
    public void ScheduleContinuationRoutesThroughCoordinatorWhenAntecedentReady()
    {
        var coordinator = new SimulationSchedulerTestHost();
        var ran = false;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var completed = System.Threading.Tasks.Task.CompletedTask;
            ControlledTaskRuntime.ScheduleContinuation(completed, () => ran = true, "test.await", flowExecutionContext: false);

            // The continuation must not run inline - it is queued on the coordinator's loop.
            Assert.False(ran);
            coordinator.Scheduler.RunUntilIdle();
            Assert.True(ran);
        });
    }

    [Fact]
    public void ScheduleYieldQueuesContinuationWithoutRunningInline()
    {
        var coordinator = new SimulationSchedulerTestHost();
        var ran = false;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ControlledTaskRuntime.ScheduleYield(() => ran = true, "test.yield", flowExecutionContext: false);
            Assert.False(ran);
            coordinator.Scheduler.RunUntilIdle();
            Assert.True(ran);
        });
    }

    [Fact]
    public void DrainUntilCompletedPumpsUntilTaskCompletes()
    {
        var coordinator = new SimulationSchedulerTestHost();
        var tcs = new System.Threading.Tasks.TaskCompletionSource();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // Completing the task is itself scheduled work; the drain must run it.
            coordinator.Scheduler.Schedule(() => tcs.SetResult());
            ControlledTaskRuntime.DrainUntilCompleted(tcs.Task, "test.wait");
            Assert.True(tcs.Task.IsCompleted);
        });
    }

    [Fact]
    public void DrainUntilCompletedThrowsDeadlockWhenTaskNeverCompletes()
    {
        var coordinator = new SimulationSchedulerTestHost();
        var tcs = new System.Threading.Tasks.TaskCompletionSource();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ControlledSynchronousWaitDeadlockException>(
                () => ControlledTaskRuntime.DrainUntilCompleted(tcs.Task, "test.wait"));
        });
    }

    [Fact]
    public void ParallelRuntimesResolveTheirOwnSchedulers()
    {
        var coordinatorA = new SimulationSchedulerTestHost(description: "A");
        var coordinatorB = new SimulationSchedulerTestHost(description: "B");

        var resolvedA = TaskTestHarness.RunInSimulation(
            coordinatorA,
            () =>
            {
                return ControlledTaskRuntime.RequireScheduler("api").Scheduler;
            });
        var resolvedB = TaskTestHarness.RunInSimulation(
            coordinatorB,
            () =>
            {
                return ControlledTaskRuntime.RequireScheduler("api").Scheduler;
            });

        Assert.Same(coordinatorA.Scheduler, resolvedA);
        Assert.Same(coordinatorB.Scheduler, resolvedB);
    }
}
