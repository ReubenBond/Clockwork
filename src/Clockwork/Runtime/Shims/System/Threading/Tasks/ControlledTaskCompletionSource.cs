using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Execution;
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
/// <para>
/// Completion is the one controlled surface which runs outside a simulation: a finalizer, or a callback from
/// uninstrumented code, may complete a source with no simulation's ambient context, and the shim then passes through to
/// the BCL member. With a simulation's ambient context, a completion publishes into that simulation, so it requires the
/// simulation's logical thread like every other controlled API and otherwise throws
/// <see cref="SimulationThreadAffinityException"/>. Each shim publishes or acquires an edge only inside a controlled
/// operation, as the race detector records edges only there.
/// </para>
/// </summary>
public static class ControlledTaskCompletionSource
{
    private const string Api = "System.Threading.Tasks.TaskCompletionSource";
    private const string GenericApi = "System.Threading.Tasks.TaskCompletionSource`1";

    /// <summary>Controlled <c>TaskCompletionSource.SetResult()</c>.</summary>
    public static void SetResult(TaskCompletionSource source)
    {
        RequireSimulationThread(Api);
        source.SetResult();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetResult()</c>.</summary>
    public static bool TrySetResult(TaskCompletionSource source)
    {
        RequireSimulationThread(Api);
        return Published(source.TrySetResult(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(Exception)</c>.</summary>
    public static void SetException(TaskCompletionSource source, Exception exception)
    {
        RequireSimulationThread(Api);
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        RequireSimulationThread(Api);
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, Exception exception)
    {
        RequireSimulationThread(Api);
        return Published(source.TrySetException(exception), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        RequireSimulationThread(Api);
        return Published(source.TrySetException(exceptions), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled()</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source)
    {
        RequireSimulationThread(Api);
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        RequireSimulationThread(Api);
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source)
    {
        RequireSimulationThread(Api);
        return Published(source.TrySetCanceled(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        RequireSimulationThread(Api);
        return Published(source.TrySetCanceled(cancellationToken), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetFromTask(Task)</c>.</summary>
    public static void SetFromTask(TaskCompletionSource source, Task completedTask)
    {
        RequireSimulationThread(Api);
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetFromTask(Task)</c>.</summary>
    public static bool TrySetFromTask(TaskCompletionSource source, Task completedTask)
    {
        RequireSimulationThread(Api);
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
        RequireSimulationThread(GenericApi);
        source.SetResult(result);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetResult(TResult)</c>.</summary>
    public static bool TrySetResult<TResult>(TaskCompletionSource<TResult> source, TResult result)
    {
        RequireSimulationThread(GenericApi);
        return Published(source.TrySetResult(result), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(Exception)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        RequireSimulationThread(GenericApi);
        source.SetException(exception);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        RequireSimulationThread(GenericApi);
        source.SetException(exceptions);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        RequireSimulationThread(GenericApi);
        return Published(source.TrySetException(exception), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        RequireSimulationThread(GenericApi);
        return Published(source.TrySetException(exceptions), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled()</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        RequireSimulationThread(GenericApi);
        source.SetCanceled();
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        RequireSimulationThread(GenericApi);
        source.SetCanceled(cancellationToken);
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        RequireSimulationThread(GenericApi);
        return Published(source.TrySetCanceled(), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        RequireSimulationThread(GenericApi);
        return Published(source.TrySetCanceled(cancellationToken), source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static void SetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        RequireSimulationThread(GenericApi);
        source.SetFromTask(completedTask);
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static bool TrySetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        RequireSimulationThread(GenericApi);
        if (!source.TrySetFromTask(completedTask))
        {
            return false;
        }

        Published(completedTask, source.Task);
        return true;
    }

    private static void RequireSimulationThread(string api, [CallerMemberName] string member = "")
    {
        if (SimulationExecutionContext.TryGetCurrentRuntime(out SimulationRuntimeIdentity? runtime)
            && !runtime!.Scheduler.IsSimulationThread)
        {
            SimulationRuntimeDispatch.ThrowThreadAffinityViolation(runtime, $"{api}.{member}");
        }
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
