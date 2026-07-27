using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Threading;

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
    /// stays controlled.
    /// </summary>
    /// <param name="antecedent">The task whose completion makes the continuation runnable.</param>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    /// <param name="flowExecutionContext">
    /// Only consulted outside a simulation, where it selects the real awaiter's context-flowing
    /// <see cref="System.Runtime.CompilerServices.INotifyCompletion.OnCompleted"/> (<see langword="true"/>)
    /// versus the non-flowing
    /// <see cref="System.Runtime.CompilerServices.ICriticalNotifyCompletion.UnsafeOnCompleted"/>
    /// (<see langword="false"/>), preserving normal BCL behaviour. Inside a simulation every continuation
    /// runs on the single logical thread, so the distinction is moot and ignored.
    /// </param>
    public static void ScheduleContinuation(
        System.Threading.Tasks.Task antecedent,
        Action continuation,
        string apiName,
        bool flowExecutionContext)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuation);

        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.ScheduleWhenReady(node, () => antecedent.IsCompleted, continuation);
        }
        else if (flowExecutionContext)
        {
            antecedent.GetAwaiter().OnCompleted(continuation);
        }
        else
        {
            antecedent.GetAwaiter().UnsafeOnCompleted(continuation);
        }
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
    /// <exception cref="ControlledTaskServiceMissingException">
    /// Thrown when a simulation is active but no coordinator is registered for its runtime.
    /// </exception>
    public static void QueueWork(Action work, string apiName)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            // Each queued unit of work is an independently-schedulable controlled strand, so it runs under
            // a fresh logical-strand identity (see Clockwork.Runtime.Threading.ControlledSynchronizationFlow).
            // That identity is what lets the controlled Monitor/Lock distinguish a reentrant acquire by the
            // owning strand from a contended acquire by a different strand, since every strand shares the one
            // cooperative logical thread. It is inert for callers that do not use those primitives.
            coordinator.Schedule(
                node,
                () => Clockwork.Runtime.Threading.ControlledSynchronizationFlow.RunAsNewStrand(work));
        }
        else
        {
            // Callers gate on IsSimulationActive before queuing controlled work; reaching here means the
            // shim mis-routed, so run inline rather than silently dropping the work.
            work();
        }
    }

    internal static void RunWithCapturedExecutionContext(ExecutionContext? context, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (context is null)
        {
            work();
            return;
        }

        long strandId = ControlledSynchronizationFlow.CurrentId;
        ExecutionContext.Run(
            context,
            static state =>
            {
                var flowed = ((long StrandId, Action Work))state!;
                ControlledSynchronizationFlow.RunAsStrand(flowed.StrandId, flowed.Work);
            },
            (strandId, work));
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
                using var runtimeScope = SimulationExecutionContext.EnterRuntime(
                    SimulationRuntimeActivation.CreateToken(),
                    invocation.Snapshot.Runtime);
                using var nodeScope = invocation.Snapshot.Node is null
                    ? null
                    : SimulationExecutionContext.EnterNode(invocation.Snapshot.Node);
                using var executionScope = invocation.Snapshot.LogicalExecutionId.IsNone
                    ? null
                    : SimulationExecutionContext.EnterLogicalExecution(invocation.Snapshot.LogicalExecutionId);
                ControlledSynchronizationFlow.RunAsStrand(invocation.StrandId, invocation.Work);
            },
            (snapshot, strandId, work));
    }

    /// <summary>
    /// Schedules <paramref name="continuation"/> to run as immediately-runnable controlled work (the
    /// backing for <c>Task.Yield</c>). Outside a simulation it falls back to a real
    /// <see cref="System.Runtime.CompilerServices.YieldAwaitable"/>, preserving the normal "resume
    /// asynchronously" semantics rather than running the continuation inline.
    /// </summary>
    /// <param name="continuation">The continuation to schedule.</param>
    /// <param name="apiName">The controlled API, for diagnostics.</param>
    /// <param name="flowExecutionContext">
    /// Only consulted outside a simulation; selects the real yield awaiter's context-flowing versus
    /// non-flowing completion registration.
    /// </param>
    public static void ScheduleYield(Action continuation, string apiName, bool flowExecutionContext)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.Schedule(node, continuation);
        }
        else if (flowExecutionContext)
        {
            default(System.Runtime.CompilerServices.YieldAwaitable).GetAwaiter().OnCompleted(continuation);
        }
        else
        {
            default(System.Runtime.CompilerServices.YieldAwaitable).GetAwaiter().UnsafeOnCompleted(continuation);
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

    internal static bool RunOne(string apiName)
    {
        return TryGetCoordinator(apiName, out var coordinator, out var node)
            ? coordinator.RunOne(node)
            : Thread.Yield();
    }

    internal static void ParkIndefinitely(string apiName)
    {
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            coordinator.ScheduleWhenReady(node, static () => false, static () => { });
        }
    }

    /// <summary>
    /// Registers a deterministic virtual-time deadline on the ambient coordinator, backing the finite
    /// timeout of a controlled synchronization wait. Assumes a simulation is active (callers gate on
    /// <see cref="IsSimulationActive"/> first); if none is, a never-elapsing handle is returned so a
    /// mis-routed caller degrades to an infinite wait rather than faulting.
    /// </summary>
    /// <param name="delay">The strictly positive modelled delay before the deadline elapses.</param>
    /// <param name="onElapsed">An optional callback invoked once, on the logical thread, when the deadline elapses.</param>
    /// <param name="apiName">The controlled API registering the timeout, for the missing-service diagnostic.</param>
    /// <returns>A handle used to observe elapse or cancel the deadline.</returns>
    public static IControlledTimeout RegisterTimeout(TimeSpan delay, Action? onElapsed, string apiName)
    {
        if (TryGetCoordinator(apiName, out var coordinator, out var node))
        {
            return coordinator.RegisterTimeout(node, delay, onElapsed);
        }

        return InertTimeout.Instance;
    }

    /// <summary>A timeout handle that never elapses, used only when no coordinator is resolved (out of simulation).</summary>
    private sealed class InertTimeout : IControlledTimeout
    {
        public static readonly InertTimeout Instance = new();

        public bool IsElapsed => false;

        public void Cancel()
        {
        }
    }
}
