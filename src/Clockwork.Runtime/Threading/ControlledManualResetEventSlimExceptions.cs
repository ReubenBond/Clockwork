namespace Clockwork.Runtime.Threading;

/// <summary>
/// Thrown when a <see cref="System.Threading.ManualResetEventSlim"/> which was not created by Clockwork is
/// used through the controlled surface.
/// </summary>
public sealed class ControlledManualResetEventSlimUnsupportedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the exception.</summary>
    public ControlledManualResetEventSlimUnsupportedException(string apiName, string reason)
        : base($"The ManualResetEventSlim API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ApiName = apiName;
    }

    /// <summary>Gets the unsupported API name.</summary>
    public string? ApiName { get; }
}
