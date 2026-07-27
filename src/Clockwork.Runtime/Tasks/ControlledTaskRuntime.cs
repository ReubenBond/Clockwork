using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Threading;

namespace Clockwork.Runtime.Tasks;

/// <summary>Bridges controlled async/task rewrite targets to the active simulation coordinator.</summary>
public static class ControlledTaskRuntime
{
    /// <summary>
    /// Gets a value indicating whether a simulation is active on the calling logical thread.
    /// </summary>
    public static bool IsSimulationActive => SimulationExecutionContext.IsActive;

    /// <summary>Resolves the coordinator for the active ambient simulation runtime.</summary>
    public static (ISimulationTaskCoordinator Coordinator, SimulationNodeIdentity? Node) RequireCoordinator(
        string apiName)
    {
        var snapshot = SimulationRuntimeDispatch.RequireActiveSimulation(apiName);
        if (!SimulationTaskCoordination.TryGet(snapshot.Runtime, out var coordinator) || coordinator is null)
        {
            throw new ControlledTaskServiceMissingException(snapshot.Runtime, apiName);
        }

        return (coordinator, snapshot.Node);
    }

    /// <summary>
    /// Registers <paramref name="continuation"/> so it runs on the simulation's logical thread once
    /// <paramref name="antecedent"/> completes, routed through the ambient coordinator's readiness queue.
    /// This is the core of "await pauses the logical operation and completion makes it runnable": the
    /// continuation is never invoked inline, never captured onto a captured
    /// <see cref="System.Threading.SynchronizationContext"/>, and never handed to the physical thread
    /// pool - it always lands on the coordinator, which is exactly why <c>ConfigureAwait(false)</c> still
    /// stays controlled.
    /// </summary>
    /// <param name="antecedent">The task whose completion makes the continuation runnable.</param>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    /// <param name="flowExecutionContext">Whether to capture the caller's user execution context.</param>
    public static void ScheduleContinuation(
        System.Threading.Tasks.Task antecedent,
        Action continuation,
        string apiName,
        bool flowExecutionContext)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuation);
        var snapshot = SimulationRuntimeDispatch.RequireActiveSimulation(apiName);
        ExecutionContext? context = flowExecutionContext ? ExecutionContext.Capture() : null;
        coordinator.ScheduleWhenReady(
            node,
            () => antecedent.IsCompleted,
            () => RunScheduledWork(snapshot, context, continuation));
    }

    /// <summary>
    /// Schedules <paramref name="work"/> as a new, immediately-runnable unit of controlled work on the
    /// ambient coordinator's ready queue. This is the shared primitive behind every Phase 6B surface that
    /// spawns a fresh logical operation - <c>Task.Run</c>, <c>TaskFactory.StartNew</c>,
    /// <c>ThreadPool.QueueUserWorkItem</c>, <c>Thread.Start</c>, and the branches of <c>Parallel</c> - so
    /// the body runs on the simulation's single logical thread interleaved with all other controlled work
    /// instead of on an uncontrolled physical thread-pool thread. The body runs to its next explicit yield
    /// point (an <c>await</c>, <c>Task.Yield</c>, <c>Thread.Yield</c>/<c>Sleep</c>, or completion) as one
    /// cooperative scheduling unit.
    /// </summary>
    /// <param name="work">The work item to enqueue. Runs exactly once.</param>
    /// <param name="apiName">The controlled API queuing the work, for the missing-service diagnostic.</param>
    /// <param name="flowExecutionContext">Whether to capture the caller's user execution context.</param>
    /// <exception cref="ControlledTaskServiceMissingException">
    /// Thrown when a simulation is active but no coordinator is registered for its runtime.
    /// </exception>
    public static void QueueWork(Action work, string apiName, bool flowExecutionContext = true)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        ArgumentNullException.ThrowIfNull(work);
        var snapshot = SimulationRuntimeDispatch.RequireActiveSimulation(apiName);
        ExecutionContext? context = flowExecutionContext ? ExecutionContext.Capture() : null;
        coordinator.Schedule(
            node,
            () => Clockwork.Runtime.Threading.ControlledSynchronizationFlow.RunAsNewStrand(
                () => RunScheduledWork(snapshot, context, work)));
    }

    internal static void RunWithCapturedExecutionContext(ExecutionContext? context, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (context is null)
        {
            work();
            return;
        }

        SimulationExecutionSnapshot snapshot = SimulationExecutionContext.Current
            ?? throw new InvalidOperationException("A controlled execution context requires an active simulation.");
        long strandId = ControlledSynchronizationFlow.CurrentId;
        ExecutionContext.Run(
            context,
            static state =>
            {
                var flowed = ((SimulationExecutionSnapshot Snapshot, long StrandId, Action Work))state!;
                RunInSimulationSnapshot(flowed.Snapshot, flowed.StrandId, flowed.Work);
            },
            (snapshot, strandId, work));
    }

    internal static void RunWithCapturedExecutionContextAsNewStrand(ExecutionContext? context, Action work) =>
        ControlledSynchronizationFlow.RunAsNewStrand(() => RunWithCapturedExecutionContext(context, work));

    internal static void RunWithoutUserExecutionContext(SimulationExecutionSnapshot snapshot, Action work)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(work);

        long strandId = ControlledSynchronizationFlow.CurrentId;
        ExecutionContext.Run(
            (ExecutionContext)Activator.CreateInstance(typeof(ExecutionContext), nonPublic: true)!,
            static state =>
            {
                var invocation = ((SimulationExecutionSnapshot Snapshot, long StrandId, Action Work))state!;
                RunInSimulationSnapshot(invocation.Snapshot, invocation.StrandId, invocation.Work);
            },
            (snapshot, strandId, work));
    }

    /// <summary>
    /// Schedules <paramref name="continuation"/> to run as immediately-runnable controlled work (the
    /// backing for <c>Task.Yield</c>).
    /// </summary>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    /// <param name="flowExecutionContext">Whether to capture the caller's user execution context.</param>
    public static void ScheduleYield(Action continuation, string apiName, bool flowExecutionContext)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        ArgumentNullException.ThrowIfNull(continuation);
        var snapshot = SimulationRuntimeDispatch.RequireActiveSimulation(apiName);
        ExecutionContext? context = flowExecutionContext ? ExecutionContext.Capture() : null;
        coordinator.Schedule(node, () => RunScheduledWork(snapshot, context, continuation));
    }

    /// <summary>
    /// Deterministically pumps ready simulation work on the calling logical thread until
    /// <paramref name="task"/> completes, backing controlled synchronous waits. Assumes a simulation is
    /// active.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="apiName">The controlled synchronous-wait API, for diagnostics.</param>
    public static void DrainUntilCompleted(System.Threading.Tasks.Task task, string apiName)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        ArgumentNullException.ThrowIfNull(task);
        coordinator.DrainUntil(node, () => task.IsCompleted);
    }

    /// <summary>
    /// Deterministically pumps ready simulation work until <paramref name="completed"/> holds. Used by
    /// multi-task synchronous waits (<c>WaitAll</c>/<c>WaitAny</c>). Assumes a simulation is active.
    /// </summary>
    /// <param name="completed">The predicate that ends the wait.</param>
    /// <param name="apiName">The controlled synchronous-wait API, for diagnostics.</param>
    public static void DrainUntil(Func<bool> completed, string apiName)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        ArgumentNullException.ThrowIfNull(completed);
        coordinator.DrainUntil(node, completed);
    }

    internal static bool RunOne(string apiName)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        return coordinator.RunOne(node);
    }

    internal static void ParkIndefinitely(string apiName)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        coordinator.ScheduleWhenReady(node, static () => false, static () => { });
    }

    /// <summary>
    /// Registers a deterministic virtual-time deadline on the ambient coordinator, backing the finite
    /// timeout of a controlled synchronization wait. Requires an active simulation.
    /// </summary>
    /// <param name="delay">The strictly positive modelled delay before the deadline elapses.</param>
    /// <param name="onElapsed">An optional callback invoked once, on the logical thread, when the deadline elapses.</param>
    /// <param name="apiName">The controlled API registering the timeout, for the missing-service diagnostic.</param>
    /// <returns>A handle used to observe elapse or cancel the deadline.</returns>
    public static IControlledTimeout RegisterTimeout(TimeSpan delay, Action? onElapsed, string apiName)
    {
        var (coordinator, node) = RequireCoordinator(apiName);
        return coordinator.RegisterTimeout(node, delay, onElapsed);
    }

    internal static IControlledTimeout RegisterTimeout(
        SimulationExecutionSnapshot snapshot,
        TimeSpan delay,
        Action? onElapsed,
        string apiName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!SimulationTaskCoordination.TryGet(snapshot.Runtime, out var coordinator) || coordinator is null)
        {
            throw new ControlledTaskServiceMissingException(snapshot.Runtime, apiName);
        }

        return coordinator.RegisterTimeout(snapshot.Node, delay, onElapsed);
    }

    internal static void QueueCapturedWork(
        SimulationExecutionSnapshot snapshot,
        ExecutionContext? context,
        Action work,
        string apiName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(work);
        if (!SimulationTaskCoordination.TryGet(snapshot.Runtime, out var coordinator) || coordinator is null)
        {
            throw new ControlledTaskServiceMissingException(snapshot.Runtime, apiName);
        }

        coordinator.Schedule(
            snapshot.Node,
            () => ControlledSynchronizationFlow.RunAsNewStrand(
                () => RunScheduledWork(snapshot, context, work)));
    }

    private static void RunScheduledWork(
        SimulationExecutionSnapshot snapshot,
        ExecutionContext? context,
        Action work)
    {
        if (context is null)
        {
            RunWithoutUserExecutionContext(snapshot, work);
            return;
        }

        RunWithCapturedExecutionContext(context, work);
    }

    private static void RunInSimulationSnapshot(
        SimulationExecutionSnapshot snapshot,
        long strandId,
        Action work)
    {
        using var runtimeScope = SimulationExecutionContext.EnterRuntime(
            SimulationRuntimeActivation.CreateToken(),
            snapshot.Runtime);
        using var nodeScope = snapshot.Node is null
            ? null
            : SimulationExecutionContext.EnterNode(snapshot.Node);
        using var executionScope = snapshot.LogicalExecutionId.IsNone
            ? null
            : SimulationExecutionContext.EnterLogicalExecution(snapshot.LogicalExecutionId);
        ControlledSynchronizationFlow.RunAsStrand(strandId, work);
    }
}
