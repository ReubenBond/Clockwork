namespace Clockwork.Runtime.Threading;

/// <summary>
/// Thrown when an application invokes an operating-system-specific <see cref="System.Threading.Thread"/>
/// operation that Clockwork's controlled threading surface cannot model faithfully inside a simulation -
/// thread priority, apartment state, and the abort/suspend/resume/interrupt family. Rather than silently
/// ignoring the call (which would change observable behaviour) or letting it reach the real OS (which is
/// non-deterministic and, for several of these, throws
/// <see cref="System.PlatformNotSupportedException"/> on modern .NET anyway), the rewritten call site
/// rejects it precisely with the reason and the phase that would own it, if any.
/// </summary>
public sealed class ControlledThreadUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledThreadUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported thread API.</param>
    /// <param name="reason">Why it cannot be modelled faithfully.</param>
    public ControlledThreadUnsupportedException(string apiName, string reason)
        : base($"The thread API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported thread API.</summary>
    public string? ApiName { get; }
}

/// <summary>
/// Thrown when an application invokes a <see cref="System.Threading.ThreadPool"/> operation that
/// Clockwork's controlled thread-pool surface cannot model faithfully inside a simulation - the native
/// I/O family (<c>UnsafeQueueNativeOverlapped</c>) and, until Phase 7 provides controlled wait handles,
/// the registered-wait family (<c>RegisterWaitForSingleObject</c> and its unsafe variant). The rewritten
/// call site rejects these precisely with the reason and the phase that would own them, rather than
/// silently ignoring the call or letting it reach uncontrolled native/OS machinery.
/// </summary>
public sealed class ControlledThreadPoolUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledThreadPoolUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported thread-pool API.</param>
    /// <param name="reason">Why it cannot be modelled faithfully.</param>
    public ControlledThreadPoolUnsupportedException(string apiName, string reason)
        : base($"The thread-pool API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported thread-pool API.</summary>
    public string? ApiName { get; }
}

/// <summary>
/// Thrown when a synchronization-context member would expose physical wait-handle behaviour that the
/// deterministic scheduler cannot model.
/// </summary>
public sealed class ControlledSynchronizationContextUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the exception.</summary>
    public ControlledSynchronizationContextUnsupportedException(string apiName, string reason)
        : base($"The synchronization-context API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported API.</summary>
    public string? ApiName { get; }
}

/// <summary>
/// Thrown when a legacy execution-context member would invoke BCL behavior outside Clockwork's controlled
/// logical-context model.
/// </summary>
public sealed class ControlledExecutionContextUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the exception.</summary>
    public ControlledExecutionContextUnsupportedException(string apiName, string reason)
        : base($"The execution-context API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported API.</summary>
    public string? ApiName { get; }
}

/// <summary>
/// Thrown when an application invokes a <see cref="System.Threading.Tasks.Parallel"/> overload that
/// Clockwork's controlled <c>Parallel</c> surface cannot model faithfully inside a simulation - the
/// overloads whose body receives a <see cref="System.Threading.Tasks.ParallelLoopState"/> (break/stop),
/// the thread-local (<c>TLocal</c>) overloads, and the <c>Partitioner</c>/<c>OrderablePartitioner</c>
/// overloads. Modelling these deterministically would require constructing framework types
/// (<c>ParallelLoopState</c>) that have no public surface, so the rewritten call site rejects them
/// precisely rather than letting the loop body run on uncontrolled thread-pool threads.
/// </summary>
public sealed class ControlledParallelUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledParallelUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported parallel API.</param>
    /// <param name="reason">Why it cannot be modelled faithfully.</param>
    public ControlledParallelUnsupportedException(string apiName, string reason)
        : base($"The parallel API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported parallel API.</summary>
    public string? ApiName { get; }
}
