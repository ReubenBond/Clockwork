using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// The base type for every exception the controlled async/task machinery raises. Distinguishes a
/// deterministic, diagnosable failure originating in Clockwork's controlled task layer from an
/// ordinary application <see cref="System.Threading.Tasks.Task"/> fault.
/// </summary>
public class ControlledTaskException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledTaskException"/> class.</summary>
    public ControlledTaskException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ControlledTaskException"/> class.</summary>
    /// <param name="message">The diagnostic message.</param>
    public ControlledTaskException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ControlledTaskException"/> class.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public ControlledTaskException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when a simulation is active but no <see cref="ISimulationTaskCoordinator"/> is registered for
/// its runtime. The controlled async machinery must never silently fall back to real thread-pool
/// scheduling inside a simulation (that would make continuations non-deterministic and let async work
/// escape the single logical thread), so a missing coordinator is a hard, explicit failure - the async
/// analogue of <see cref="Clockwork.Runtime.Shims.SimulationServiceMissingException"/>.
/// </summary>
public sealed class ControlledTaskServiceMissingException : ControlledTaskException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledTaskServiceMissingException"/> class.</summary>
    /// <param name="runtime">The active runtime that has no coordinator registered.</param>
    /// <param name="apiName">The controlled API that required a coordinator.</param>
    public ControlledTaskServiceMissingException(SimulationRuntimeIdentity runtime, string apiName)
        : base(BuildMessage(runtime, apiName))
    {
        Runtime = runtime;
        ApiName = apiName;
    }

    /// <summary>Gets the active runtime that had no coordinator registered.</summary>
    public SimulationRuntimeIdentity? Runtime { get; }

    /// <summary>Gets the controlled API that required a coordinator.</summary>
    public string? ApiName { get; }

    private static string BuildMessage(SimulationRuntimeIdentity runtime, string apiName)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return $"The controlled task API '{apiName}' ran inside active simulation runtime '{runtime.Id}', " +
            "but no ISimulationTaskCoordinator is registered for it. A simulation host must register a " +
            "task coordinator (see SimulationTaskCoordination.Register) before controlled async or task " +
            "APIs execute; the machinery refuses to fall back to real thread-pool scheduling inside a " +
            "simulation because that would be non-deterministic.";
    }
}

/// <summary>
/// Thrown by a controlled synchronous wait (<c>task.Wait()</c>, <c>task.Result</c>,
/// <c>Task.WaitAll</c>/<c>WaitAny</c>) when the simulation's ready work is exhausted yet the awaited
/// task has still not completed. Because the simulation runs on a single logical thread that the wait
/// itself is pumping, this is the deterministic, immediately-diagnosable signature of a deadlock rather
/// than a real-time hang.
/// </summary>
public sealed class ControlledSynchronousWaitDeadlockException : ControlledTaskException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledSynchronousWaitDeadlockException"/> class.</summary>
    /// <param name="apiName">The synchronous-wait API that deadlocked.</param>
    public ControlledSynchronousWaitDeadlockException(string apiName)
        : base(
            $"The controlled synchronous wait '{apiName}' cannot complete: the simulation has no more ready " +
            "work to run yet the awaited task is still not complete. In the single-threaded simulation this " +
            "is a deadlock - the work that would complete the task can never run because this blocking wait " +
            "is occupying the only logical thread. Prefer awaiting the task instead of blocking on it.")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the synchronous-wait API that deadlocked.</summary>
    public string? ApiName { get; }
}

/// <summary>
/// Thrown when a controlled task API that Phase&#160;6A deliberately does not support is invoked inside a
/// simulation - for example <c>Task.Delay</c> (owned by the virtual-timer phase) or <c>Task.Run</c>
/// (thread-pool scheduling, owned by the threading phase). The call site is rewritten to this precise
/// rejection rather than being allowed to silently use wall-clock time or an uncontrolled thread.
/// </summary>
public sealed class ControlledTaskUnsupportedException : ControlledTaskException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledTaskUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported API.</param>
    /// <param name="reason">Why it is unsupported and which phase owns it.</param>
    public ControlledTaskUnsupportedException(string apiName, string reason)
        : base($"The task API '{apiName}' is not controlled by Clockwork's Phase 6A async machinery: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported API.</summary>
    public string? ApiName { get; }
}
