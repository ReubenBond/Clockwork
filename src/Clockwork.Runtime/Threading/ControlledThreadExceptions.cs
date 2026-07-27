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
