using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// A controlled <see cref="System.Threading.Tasks.TaskCompletionSource"/>. Application code creates
/// completion sources to hand out a <see cref="Task"/> it will complete later; the rewriter substitutes
/// this type so that construction stays deterministic inside a simulation.
/// </para>
/// <para>
/// The one behavioural change is that <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>
/// is stripped inside a simulation. That flag normally makes completion post the task's continuations to
/// the thread pool - an escape from the single logical thread. It is safe to drop because Clockwork
/// controls continuations at the await/ContinueWith site (via the controlled awaiters and
/// <see cref="ControlledTask.ContinueWith(Task, Action{Task})"/>), so completion here simply sets status
/// on the logical thread and the controlled awaiter reschedules deterministically. Outside a simulation
/// the flag - and every other option - is honoured exactly.
/// </para>
/// </summary>
public sealed class ControlledTaskCompletionSource
{
    private readonly TaskCompletionSource _inner;

    /// <summary>Initializes a new controlled completion source with no options.</summary>
    public ControlledTaskCompletionSource()
        : this(TaskCreationOptions.None)
    {
    }

    /// <summary>Initializes a new controlled completion source with the given creation options.</summary>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is dropped inside a simulation).</param>
    public ControlledTaskCompletionSource(TaskCreationOptions creationOptions) =>
        _inner = new TaskCompletionSource(ControlledTaskCompletionSourceOptions.Normalize(creationOptions));

    /// <summary>Gets the task controlled by this completion source.</summary>
    public Task Task => _inner.Task;

    /// <summary>Transitions the task to <see cref="TaskStatus.RanToCompletion"/>.</summary>
    public void SetResult() => _inner.SetResult();

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.RanToCompletion"/>.</summary>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetResult() => _inner.TrySetResult();

    /// <summary>Transitions the task to <see cref="TaskStatus.Faulted"/> with the given exception.</summary>
    /// <param name="exception">The fault.</param>
    public void SetException(Exception exception) => _inner.SetException(exception);

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.Faulted"/>.</summary>
    /// <param name="exception">The fault.</param>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetException(Exception exception) => _inner.TrySetException(exception);

    /// <summary>Transitions the task to <see cref="TaskStatus.Canceled"/>.</summary>
    public void SetCanceled() => _inner.SetCanceled();

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.Canceled"/>.</summary>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetCanceled() => _inner.TrySetCanceled();
}

/// <summary>
/// A controlled <see cref="System.Threading.Tasks.TaskCompletionSource{TResult}"/>. See
/// <see cref="ControlledTaskCompletionSource"/> for the design; this variant completes a
/// <see cref="Task{TResult}"/> with a result.
/// </summary>
/// <typeparam name="TResult">The result type.</typeparam>
public sealed class ControlledTaskCompletionSource<TResult>
{
    private readonly TaskCompletionSource<TResult> _inner;

    /// <summary>Initializes a new controlled completion source with no options.</summary>
    public ControlledTaskCompletionSource()
        : this(TaskCreationOptions.None)
    {
    }

    /// <summary>Initializes a new controlled completion source with the given creation options.</summary>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> is dropped inside a simulation).</param>
    public ControlledTaskCompletionSource(TaskCreationOptions creationOptions) =>
        _inner = new TaskCompletionSource<TResult>(ControlledTaskCompletionSourceOptions.Normalize(creationOptions));

    /// <summary>Gets the task controlled by this completion source.</summary>
    public Task<TResult> Task => _inner.Task;

    /// <summary>Transitions the task to <see cref="TaskStatus.RanToCompletion"/> with the given result.</summary>
    /// <param name="result">The result.</param>
    public void SetResult(TResult result) => _inner.SetResult(result);

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.RanToCompletion"/> with the given result.</summary>
    /// <param name="result">The result.</param>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetResult(TResult result) => _inner.TrySetResult(result);

    /// <summary>Transitions the task to <see cref="TaskStatus.Faulted"/> with the given exception.</summary>
    /// <param name="exception">The fault.</param>
    public void SetException(Exception exception) => _inner.SetException(exception);

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.Faulted"/>.</summary>
    /// <param name="exception">The fault.</param>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetException(Exception exception) => _inner.TrySetException(exception);

    /// <summary>Transitions the task to <see cref="TaskStatus.Canceled"/>.</summary>
    public void SetCanceled() => _inner.SetCanceled();

    /// <summary>Attempts to transition the task to <see cref="TaskStatus.Canceled"/>.</summary>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool TrySetCanceled() => _inner.TrySetCanceled();
}

internal static class ControlledTaskCompletionSourceOptions
{
    public static TaskCreationOptions Normalize(TaskCreationOptions creationOptions)
    {
        // Inside a simulation, dropping RunContinuationsAsynchronously keeps completion on the logical
        // thread; continuations are already controlled at the await/ContinueWith site. Outside a
        // simulation the options are passed through untouched.
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            return creationOptions & ~TaskCreationOptions.RunContinuationsAsynchronously;
        }

        return creationOptions;
    }
}
