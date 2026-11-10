using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Racing;

namespace Clockwork.Shims.System.Threading.Tasks;

/// <summary>
/// <para>
/// Controlled completion members of <see cref="TaskCompletionSource"/> and <see cref="TaskCompletionSource{TResult}"/>.
/// Rewritten code keeps the real BCL completion sources, with their real <see cref="TaskCreationOptions"/>: the
/// rewriter redirects only calls to the completion members, each to a static shim whose first parameter is the
/// receiving source. Each shim completes the source through the BCL member itself, so validation, exceptions, and the
/// resulting task state are the BCL's, and the completion members are not virtual, so a source derived from the BCL
/// type completes exactly as it would without the redirect.
/// </para>
/// <para>
/// A successful completion publishes a race-detection release edge on the source's task, which an await, a
/// synchronous wait, or a controlled combinator or continuation acquires when it observes the task complete. A
/// completion from another task (<c>SetFromTask</c>) first acquires that task's edges, since it reads that task's
/// outcome. Continuation scheduling needs no shim: Clockwork never registers a BCL continuation on a task, so completing
/// a source runs nothing inline and posts nothing to the thread pool, whatever its options.
/// </para>
/// <para>
/// Unlike the other controlled shims, these require no active simulation: a finalizer, or a callback from uninstrumented
/// code, may complete a source outside one. Each shim then passes through to the BCL member, and publishes or acquires an
/// edge only inside a controlled operation, as the race detector records edges only there.
/// </para>
/// </summary>
public static class ControlledTaskCompletionSource
{
    /// <summary>Controlled <c>TaskCompletionSource.SetResult()</c>.</summary>
    public static void SetResult(TaskCompletionSource source)
    {
        source.SetResult();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetResult()</c>.</summary>
    public static bool TrySetResult(TaskCompletionSource source) =>
        Published(source.TrySetResult(), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetException(Exception)</c>.</summary>
    public static void SetException(TaskCompletionSource source, Exception exception)
    {
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, Exception exception) =>
        Published(source.TrySetException(exception), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, IEnumerable<Exception> exceptions) =>
        Published(source.TrySetException(exceptions), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled()</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source)
    {
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source) =>
        Published(source.TrySetCanceled(), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source, CancellationToken cancellationToken) =>
        Published(source.TrySetCanceled(cancellationToken), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetFromTask(Task)</c>.</summary>
    public static void SetFromTask(TaskCompletionSource source, Task completedTask)
    {
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetFromTask(Task)</c>.</summary>
    public static bool TrySetFromTask(TaskCompletionSource source, Task completedTask)
    {
        if (!source.TrySetFromTask(completedTask))
        {
            return false;
        }

        Published(completedTask, source.Task);
        return true;
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetResult(TResult)</c>.</summary>
    public static void SetResult<TResult>(TaskCompletionSource<TResult> source, TResult result)
    {
        source.SetResult(result);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetResult(TResult)</c>.</summary>
    public static bool TrySetResult<TResult>(TaskCompletionSource<TResult> source, TResult result) =>
        Published(source.TrySetResult(result), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(Exception)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, Exception exception) =>
        Published(source.TrySetException(exception), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions) =>
        Published(source.TrySetException(exceptions), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled()</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source) =>
        Published(source.TrySetCanceled(), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken) =>
        Published(source.TrySetCanceled(cancellationToken), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static void SetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static bool TrySetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        if (!source.TrySetFromTask(completedTask))
        {
            return false;
        }

        Published(completedTask, source.Task);
        return true;
    }

    private static void Published(Task task) => RaceSynchronization.Signal(task);

    private static bool Published(bool completed, Task task)
    {
        if (completed)
        {
            RaceSynchronization.Signal(task);
        }

        return completed;
    }

    private static void Published(Task antecedent, Task task)
    {
        RaceSynchronization.Wait(antecedent);
        RaceSynchronization.Signal(task);
    }
}
