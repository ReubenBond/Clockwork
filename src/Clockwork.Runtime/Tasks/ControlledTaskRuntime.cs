using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// The single dispatch primitive every controlled async/task shim uses to decide what to do, and the
/// bridge from the ambient <see cref="SimulationExecutionContext"/> to the registered
/// <see cref="ISimulationTaskCoordinator"/>. It encodes the deterministic contract that mirrors
/// <see cref="Clockwork.Runtime.Shims.SimulationRuntimeDispatch"/>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>No simulation active</b> - the controlled shim runs the real BCL behaviour (production
/// pass-through, a single cheap <see cref="SimulationExecutionContext.IsActive"/> read).
/// </description></item>
/// <item><description>
/// <b>Simulation active and a coordinator is registered</b> - continuations and synchronous waits are
/// routed through the coordinator so async work stays on the simulation's single logical thread.
/// </description></item>
/// <item><description>
/// <b>Simulation active but no coordinator registered</b> - throws
/// <see cref="ControlledTaskServiceMissingException"/>; the machinery never silently falls back to real
/// thread-pool scheduling inside a simulation.
/// </description></item>
/// </list>
/// </summary>
public static class ControlledTaskRuntime
{
    /// <summary>
    /// Gets a value indicating whether a simulation is active on the calling logical thread. When
    /// <see langword="false"/> every controlled shim delegates to the real BCL API unchanged. This is
    /// the recommended cheap fast-path gate.
    /// </summary>
    public static bool IsSimulationActive => SimulationExecutionContext.IsActive;

    /// <summary>
    /// Resolves the coordinator for the ambient simulation runtime, if a simulation is active. See the
    /// type remarks for the exact three-way contract.
    /// </summary>
    /// <param name="apiName">The controlled API, used only for the diagnostic when a coordinator is missing.</param>
    /// <param name="coordinator">The resolved coordinator when the controlled path applies.</param>
    /// <param name="node">The active node identity (may be <see langword="null"/> for cluster-level work).</param>
    /// <returns><see langword="true"/> if the shim should take the controlled path; <see langword="false"/> to run the real BCL API.</returns>
    /// <exception cref="ControlledTaskServiceMissingException">
    /// Thrown when a simulation is active but no coordinator is registered for its runtime.
    /// </exception>
    public static bool TryGetCoordinator(
        string apiName,
        out ISimulationTaskCoordinator coordinator,
        out SimulationNodeIdentity? node)
    {
        var snapshot = SimulationExecutionContext.Current;
        if (snapshot is null)
        {
            coordinator = null!;
            node = null;
            return false;
        }

        if (!SimulationTaskCoordination.TryGet(snapshot.Runtime, out var resolved) || resolved is null)
        {
            throw new ControlledTaskServiceMissingException(snapshot.Runtime, apiName);
        }

        coordinator = resolved;
        node = snapshot.Node;
        return true;
    }

    /// <summary>
    /// Registers <paramref name="continuation"/> so it runs on the simulation's logical thread once
    /// <paramref name="antecedent"/> completes, routed through the ambient coordinator's readiness queue.
    /// This is the core of "await pauses the logical operation and completion makes it runnable": the
    /// continuation is never invoked inline, never captured onto a captured
    /// <see cref="System.Threading.SynchronizationContext"/>, and never handed to the physical thread
    /// pool - it always lands on the coordinator, which is exactly why <c>ConfigureAwait(false)</c> still
    /// stays controlled. Assumes a simulation is active (callers gate on <see cref="IsSimulationActive"/>).
    /// </summary>
    /// <param name="antecedent">The task whose completion makes the continuation runnable.</param>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    public static void ScheduleContinuation(System.Threading.Tasks.Task antecedent, Action continuation, string apiName)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuation);

        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.ScheduleWhenReady(node, () => antecedent.IsCompleted, continuation);
        }
        else
        {
            // No simulation active: preserve real behaviour by completing the continuation the normal
            // (unsafe, non-capturing) way.
            antecedent.GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }

    /// <summary>
    /// Schedules <paramref name="continuation"/> to run as immediately-runnable controlled work (the
    /// backing for <c>Task.Yield</c>). Assumes a simulation is active.
    /// </summary>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    public static void ScheduleYield(Action continuation, string apiName)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.Schedule(node, continuation);
        }
        else
        {
            continuation();
        }
    }

    /// <summary>
    /// Deterministically pumps ready simulation work on the calling logical thread until
    /// <paramref name="task"/> completes, backing controlled synchronous waits. Assumes a simulation is
    /// active (callers gate on <see cref="IsSimulationActive"/> first).
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    /// <param name="apiName">The controlled synchronous-wait API, for diagnostics.</param>
    public static void DrainUntilCompleted(System.Threading.Tasks.Task task, string apiName)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.DrainUntil(node, () => task.IsCompleted);
        }
    }

    /// <summary>
    /// Deterministically pumps ready simulation work until <paramref name="completed"/> holds. Used by
    /// multi-task synchronous waits (<c>WaitAll</c>/<c>WaitAny</c>). Assumes a simulation is active.
    /// </summary>
    /// <param name="completed">The predicate that ends the wait.</param>
    /// <param name="apiName">The controlled synchronous-wait API, for diagnostics.</param>
    public static void DrainUntil(Func<bool> completed, string apiName)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.DrainUntil(node, completed);
        }
    }
}
