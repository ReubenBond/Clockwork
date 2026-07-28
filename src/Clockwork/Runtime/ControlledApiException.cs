namespace Clockwork.Runtime;

/// <summary>Identifies the controlled API surface that rejected an unsupported operation.</summary>
public enum ControlledApiCategory
{
    /// <summary>Controlled task and async APIs.</summary>
    Task,

    /// <summary>Controlled thread APIs.</summary>
    Thread,

    /// <summary>Controlled thread-pool APIs.</summary>
    ThreadPool,

    /// <summary>Controlled synchronization-context APIs.</summary>
    SynchronizationContext,

    /// <summary>Controlled execution-context APIs.</summary>
    ExecutionContext,

    /// <summary>Controlled parallel-loop APIs.</summary>
    Parallel,

    /// <summary>Controlled wait-handle, event, mutex, and semaphore APIs.</summary>
    WaitHandle,

    /// <summary>Controlled lightweight semaphore APIs.</summary>
    SemaphoreSlim,

    /// <summary>Controlled lightweight manual-reset-event APIs.</summary>
    ManualResetEventSlim,

    /// <summary>Controlled timer and time-provider APIs.</summary>
    Timer,
}

/// <summary>
/// Thrown when Clockwork cannot model an API faithfully inside a simulation. The structured category,
/// API name, and reason replace API-specific exception classes while preserving actionable diagnostics.
/// </summary>
public sealed class ControlledApiException : InvalidOperationException
{
    /// <summary>Initializes a new unsupported controlled-API failure.</summary>
    /// <param name="category">The controlled API surface.</param>
    /// <param name="apiName">The unsupported API.</param>
    /// <param name="reason">Why Clockwork cannot model the API faithfully.</param>
    public ControlledApiException(ControlledApiCategory category, string apiName, string reason)
        : base($"The {FormatCategory(category)} API '{apiName}' is not supported inside a Clockwork simulation: {reason}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Category = category;
        ApiName = apiName;
        Reason = reason;
    }

    /// <summary>Gets the controlled API surface.</summary>
    public ControlledApiCategory Category { get; }

    /// <summary>Gets the unsupported API.</summary>
    public string ApiName { get; }

    /// <summary>Gets why Clockwork cannot model the API faithfully.</summary>
    public string Reason { get; }

    private static string FormatCategory(ControlledApiCategory category) =>
        category switch
        {
            ControlledApiCategory.ThreadPool => "thread-pool",
            ControlledApiCategory.SynchronizationContext => "synchronization-context",
            ControlledApiCategory.ExecutionContext => "execution-context",
            ControlledApiCategory.WaitHandle => "wait-handle",
            ControlledApiCategory.SemaphoreSlim => "semaphore",
            ControlledApiCategory.ManualResetEventSlim => "ManualResetEventSlim",
            _ => category.ToString().ToLowerInvariant(),
        };
}
