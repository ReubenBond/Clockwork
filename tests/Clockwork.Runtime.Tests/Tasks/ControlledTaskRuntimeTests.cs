using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests.Tasks;

/// <summary>
/// Covers the token-gated <see cref="SimulationTaskCoordination"/> registry and the
/// <see cref="ControlledTaskRuntime"/> three-way dispatch contract (inactive, active+registered,
/// active+missing), plus continuation and synchronous-wait routing through a registered coordinator.
/// </summary>
public sealed class ControlledTaskRuntimeTests
{
    [Fact]
    public void RegisterRequiresNonNullArguments()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = TaskTestHarness.NewRuntime();
        var coordinator = new ControlledTaskLoopCoordinator();

        Assert.Throws<ArgumentNullException>(() => SimulationTaskCoordination.Register(null!, runtime, coordinator));
        Assert.Throws<ArgumentNullException>(() => SimulationTaskCoordination.Register(token, null!, coordinator));
        Assert.Throws<ArgumentNullException>(() => SimulationTaskCoordination.Register(token, runtime, null!));
    }

    [Fact]
    public void RegisterThenTryGetResolvesAndDisposeUnregisters()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = TaskTestHarness.NewRuntime();
        var coordinator = new ControlledTaskLoopCoordinator();

        var registration = SimulationTaskCoordination.Register(token, runtime, coordinator);
        try
        {
            Assert.True(SimulationTaskCoordination.TryGet(runtime, out var resolved));
            Assert.Same(coordinator, resolved);
        }
        finally
        {
            registration.Dispose();
        }

        Assert.False(SimulationTaskCoordination.TryGet(runtime, out var afterDispose));
        Assert.Null(afterDispose);
    }

    [Fact]
    public void RegisteringTwiceForTheSameRuntimeThrows()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = TaskTestHarness.NewRuntime();
        var coordinator = new ControlledTaskLoopCoordinator();

        using (SimulationTaskCoordination.Register(token, runtime, coordinator))
        {
            Assert.Throws<InvalidOperationException>(
                () => SimulationTaskCoordination.Register(token, runtime, coordinator));
        }
    }

    [Fact]
    public void TryGetCoordinatorReturnsFalseOutsideSimulation()
    {
        Assert.False(ControlledTaskRuntime.IsSimulationActive);
        Assert.False(ControlledTaskRuntime.TryGetCoordinator("test.api", out _, out var node));
        Assert.Null(node);
    }

    [Fact]
    public void TryGetCoordinatorReturnsTrueWithNodeWhenActiveAndRegistered()
    {
        var coordinator = new ControlledTaskLoopCoordinator();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.True(ControlledTaskRuntime.TryGetCoordinator("test.api", out var resolved, out var node));
            Assert.Same(coordinator, resolved);
            Assert.NotNull(node);
            Assert.Equal(TaskTestHarness.DefaultNodeAddress, node!.Address);
        });
    }

    [Fact]
    public void TryGetCoordinatorThrowsWhenActiveButNoCoordinatorRegistered()
    {
        TaskTestHarness.RunInSimulationWithoutCoordinator(() =>
        {
            var ex = Assert.Throws<ControlledTaskServiceMissingException>(
                () => ControlledTaskRuntime.TryGetCoordinator("System.Example.Api", out _, out _));
            Assert.Equal("System.Example.Api", ex.ApiName);
        });
    }

    [Fact]
    public void ScheduleContinuationRoutesThroughCoordinatorWhenAntecedentReady()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var ran = false;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            var completed = System.Threading.Tasks.Task.CompletedTask;
            ControlledTaskRuntime.ScheduleContinuation(completed, () => ran = true, "test.await", flowExecutionContext: false);

            // The continuation must not run inline - it is queued on the coordinator's loop.
            Assert.False(ran);
            coordinator.Loop.RunUntilIdle();
            Assert.True(ran);
        });
    }

    [Fact]
    public void ScheduleYieldQueuesContinuationWithoutRunningInline()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var ran = false;

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            ControlledTaskRuntime.ScheduleYield(() => ran = true, "test.yield", flowExecutionContext: false);
            Assert.False(ran);
            coordinator.Loop.RunUntilIdle();
            Assert.True(ran);
        });
    }

    [Fact]
    public void DrainUntilCompletedPumpsUntilTaskCompletes()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var tcs = new System.Threading.Tasks.TaskCompletionSource();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            // Completing the task is itself scheduled work; the drain must run it.
            coordinator.Loop.Schedule(() => tcs.SetResult());
            ControlledTaskRuntime.DrainUntilCompleted(tcs.Task, "test.wait");
            Assert.True(tcs.Task.IsCompleted);
        });
    }

    [Fact]
    public void DrainUntilCompletedThrowsDeadlockWhenTaskNeverCompletes()
    {
        var coordinator = new ControlledTaskLoopCoordinator();
        var tcs = new System.Threading.Tasks.TaskCompletionSource();

        TaskTestHarness.RunInSimulation(coordinator, () =>
        {
            Assert.Throws<ControlledSynchronousWaitDeadlockException>(
                () => ControlledTaskRuntime.DrainUntilCompleted(tcs.Task, "test.wait"));
        });
    }

    [Fact]
    public void ParallelRuntimesResolveTheirOwnCoordinators()
    {
        var coordinatorA = new ControlledTaskLoopCoordinator();
        var coordinatorB = new ControlledTaskLoopCoordinator();
        var runtimeA = TaskTestHarness.NewRuntime(description: "A");
        var runtimeB = TaskTestHarness.NewRuntime(description: "B");

        var resolvedA = TaskTestHarness.RunInSimulation(
            coordinatorA,
            () =>
            {
                ControlledTaskRuntime.TryGetCoordinator("api", out var c, out _);
                return c;
            },
            runtime: runtimeA);
        var resolvedB = TaskTestHarness.RunInSimulation(
            coordinatorB,
            () =>
            {
                ControlledTaskRuntime.TryGetCoordinator("api", out var c, out _);
                return c;
            },
            runtime: runtimeB);

        Assert.Same(coordinatorA, resolvedA);
        Assert.Same(coordinatorB, resolvedB);
    }
}
