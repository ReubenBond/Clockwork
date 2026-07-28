namespace Clockwork.Runtime.Shims;

/// <summary>
/// The base type for exceptions thrown by a deterministic BCL shim while a simulation is active.
/// Catching this type catches policy-rejected calls such as
/// <see cref="SimulationRejectedCallException"/> without catching unrelated framework exceptions.
/// </summary>
public abstract class SimulationShimException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="SimulationShimException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    protected SimulationShimException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SimulationShimException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    protected SimulationShimException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
