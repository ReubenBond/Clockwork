using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Shims;

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
/// </summary>
public static class ControlledTaskCompletionSource
{
    private const string Api = "System.Threading.Tasks.TaskCompletionSource";
    private const string GenericApi = "System.Threading.Tasks.TaskCompletionSource`1";

    /// <summary>Controlled <c>TaskCompletionSource.SetResult()</c>.</summary>
    public static void SetResult(TaskCompletionSource source)
    {
        RequireActive();
        source.SetResult();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetResult()</c>.</summary>
    public static bool TrySetResult(TaskCompletionSource source)
    {
        RequireActive();
        return Published(source.TrySetResult(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(Exception)</c>.</summary>
    public static void SetException(TaskCompletionSource source, Exception exception)
    {
        RequireActive();
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        RequireActive();
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, Exception exception)
    {
        RequireActive();
        return Published(source.TrySetException(exception), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        RequireActive();
        return Published(source.TrySetException(exceptions), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled()</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source)
    {
        RequireActive();
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        RequireActive();
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source)
    {
        RequireActive();
        return Published(source.TrySetCanceled(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        RequireActive();
        return Published(source.TrySetCanceled(cancellationToken), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetFromTask(Task)</c>.</summary>
    public static void SetFromTask(TaskCompletionSource source, Task completedTask)
    {
        RequireActive();
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetFromTask(Task)</c>.</summary>
    public static bool TrySetFromTask(TaskCompletionSource source, Task completedTask)
    {
        RequireActive();
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
        RequireGenericActive();
        source.SetResult(result);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetResult(TResult)</c>.</summary>
    public static bool TrySetResult<TResult>(TaskCompletionSource<TResult> source, TResult result)
    {
        RequireGenericActive();
        return Published(source.TrySetResult(result), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(Exception)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        RequireGenericActive();
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        RequireGenericActive();
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        RequireGenericActive();
        return Published(source.TrySetException(exception), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        RequireGenericActive();
        return Published(source.TrySetException(exceptions), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled()</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        RequireGenericActive();
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        RequireGenericActive();
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        RequireGenericActive();
        return Published(source.TrySetCanceled(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        RequireGenericActive();
        return Published(source.TrySetCanceled(cancellationToken), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static void SetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        RequireGenericActive();
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static bool TrySetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        RequireGenericActive();
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

    private static void RequireActive() => SimulationRuntimeDispatch.RequireActiveSimulation(Api);

    private static void RequireGenericActive() => SimulationRuntimeDispatch.RequireActiveSimulation(GenericApi);
}
