namespace Clockwork;

/// <summary>
/// A disposable scope that restores the previous synchronization context when disposed.
/// </summary>
/// <remarks>
/// This type is only used as a disposable scope.
/// </remarks>
internal sealed class SynchronizationContextScope : IDisposable
{
    private readonly SynchronizationContext? _previous;
    private readonly bool _shouldRestore;

    /// <summary>
    /// Gets an empty scope that does nothing when disposed.
    /// </summary>
    internal static SynchronizationContextScope Empty { get; } = new();

    private SynchronizationContextScope()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SynchronizationContextScope"/> class.
    /// Creates a new scope that will restore the specified context when disposed.
    /// </summary>
    /// <param name="previous">The synchronization context to restore.</param>
    internal SynchronizationContextScope(SynchronizationContext? previous)
    {
        _previous = previous;
        _shouldRestore = true;
    }

    /// <summary>
    /// Restores the previous synchronization context if this scope should restore.
    /// </summary>
    public void Dispose()
    {
        if (_shouldRestore)
        {
            SynchronizationContext.SetSynchronizationContext(_previous);
        }
    }
}
