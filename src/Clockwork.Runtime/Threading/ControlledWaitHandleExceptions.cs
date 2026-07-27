namespace Clockwork.Runtime.Threading;

/// <summary>
/// Thrown when an application invokes a wait-handle or event member that Clockwork's controlled surface
/// cannot model faithfully inside a simulation. This covers the raw OS-handle accessors
/// (<c>WaitHandle.Handle</c>/<c>SafeWaitHandle</c>), the named / cross-process event APIs
/// (<c>EventWaitHandle.OpenExisting</c>/<c>TryOpenExisting</c> and the named constructors), and a
/// <c>WaitOne</c>/<c>Set</c>/<c>Reset</c> issued against a wait handle the controlled surface never created
/// (so it has no modelled signalled state). Rather than escaping the deterministic scheduler onto a real
/// kernel object, the rewritten call site rejects the operation precisely with the reason.
/// </summary>
public sealed class ControlledWaitHandleUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledWaitHandleUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported wait-handle API.</param>
    /// <param name="reason">Why it cannot be modelled faithfully.</param>
    public ControlledWaitHandleUnsupportedException(string apiName, string reason)
        : base($"The wait-handle API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported wait-handle API.</summary>
    public string? ApiName { get; }
}
