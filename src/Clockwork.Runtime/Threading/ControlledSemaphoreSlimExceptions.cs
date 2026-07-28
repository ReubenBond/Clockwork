namespace Clockwork.Runtime.Threading;

/// <summary>
/// Thrown when an application invokes a <see cref="System.Threading.SemaphoreSlim"/> member that
/// Clockwork's controlled semaphore surface cannot model faithfully inside a simulation. Currently this
/// is only <see cref="System.Threading.SemaphoreSlim.AvailableWaitHandle"/>, which materialises a real
/// <see cref="System.Threading.WaitHandle"/> - an OS synchronization object owned by wait-handle and atomic control. Rather
/// than handing back an uncontrolled kernel handle (which would let code block the physical thread
/// outside the deterministic scheduler), the rewritten call site rejects it precisely with the reason
/// and the unsupported capability.
/// </summary>
public sealed class ControlledSemaphoreSlimUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledSemaphoreSlimUnsupportedException"/> class.</summary>
    /// <param name="apiName">The unsupported semaphore API.</param>
    /// <param name="reason">Why it cannot be modelled faithfully.</param>
    public ControlledSemaphoreSlimUnsupportedException(string apiName, string reason)
        : base($"The semaphore API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported semaphore API.</summary>
    public string? ApiName { get; }
}
