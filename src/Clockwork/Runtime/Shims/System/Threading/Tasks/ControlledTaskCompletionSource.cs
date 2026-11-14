using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

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
/// outcome.
/// </para>
/// <para>
/// Clockwork registers no BCL continuation on a task, so a completion runs only continuations which uninstrumented code
/// registered, and each shim completes the source as on a thread-pool thread, with no synchronization context and the
/// default scheduler current (see <see cref="ThreadPoolLocation"/>). The BCL therefore runs a framework continuation
/// which a completion in a scheduler lane's work, or in a task running visibly on a simulation scheduler, would
/// otherwise queue to the real thread pool inline on the logical thread, as it does for a completion in a controlled
/// operation.
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
    private const string TaskCompletionSourceApi = "System.Threading.Tasks.TaskCompletionSource";
    private const string GenericTaskCompletionSourceApi = "System.Threading.Tasks.TaskCompletionSource`1";

    /// <summary>Controlled <c>TaskCompletionSource.SetResult()</c>.</summary>
    public static void SetResult(TaskCompletionSource source)
    {
        Complete(TaskCompletionSourceApi, source, static source => source.SetResult());
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetResult()</c>.</summary>
    public static bool TrySetResult(TaskCompletionSource source) =>
        Published(TryComplete(TaskCompletionSourceApi, source, static source => source.TrySetResult()), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetException(Exception)</c>.</summary>
    public static void SetException(TaskCompletionSource source, Exception exception)
    {
        Complete(TaskCompletionSourceApi, (source, exception), static s => s.source.SetException(s.exception));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException(TaskCompletionSource source, IEnumerable<Exception> exceptions)
    {
        Complete(TaskCompletionSourceApi, (source, exceptions), static s => s.source.SetException(s.exceptions));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, Exception exception) =>
        Published(
            TryComplete(
                TaskCompletionSourceApi,
                (source, exception),
                static s => s.source.TrySetException(s.exception)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException(TaskCompletionSource source, IEnumerable<Exception> exceptions) =>
        Published(
            TryComplete(
                TaskCompletionSourceApi,
                (source, exceptions),
                static s => s.source.TrySetException(s.exceptions)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled()</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source)
    {
        Complete(TaskCompletionSourceApi, source, static source => source.SetCanceled());
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled(TaskCompletionSource source, CancellationToken cancellationToken)
    {
        Complete(
            TaskCompletionSourceApi,
            (source, cancellationToken),
            static s => s.source.SetCanceled(s.cancellationToken));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source) =>
        Published(TryComplete(TaskCompletionSourceApi, source, static source => source.TrySetCanceled()), source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled(TaskCompletionSource source, CancellationToken cancellationToken) =>
        Published(
            TryComplete(
                TaskCompletionSourceApi,
                (source, cancellationToken),
                static s => s.source.TrySetCanceled(s.cancellationToken)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource.SetFromTask(Task)</c>.</summary>
    public static void SetFromTask(TaskCompletionSource source, Task completedTask)
    {
        Complete(TaskCompletionSourceApi, (source, completedTask), static s => s.source.SetFromTask(s.completedTask));
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource.TrySetFromTask(Task)</c>.</summary>
    public static bool TrySetFromTask(TaskCompletionSource source, Task completedTask)
    {
        if (!TryComplete(
            TaskCompletionSourceApi,
            (source, completedTask),
            static s => s.source.TrySetFromTask(s.completedTask)))
        {
            return false;
        }

        Published(completedTask, source.Task);
        return true;
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetResult(TResult)</c>.</summary>
    public static void SetResult<TResult>(TaskCompletionSource<TResult> source, TResult result)
    {
        Complete(GenericTaskCompletionSourceApi, (source, result), static s => s.source.SetResult(s.result));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetResult(TResult)</c>.</summary>
    public static bool TrySetResult<TResult>(TaskCompletionSource<TResult> source, TResult result) =>
        Published(
            TryComplete(GenericTaskCompletionSourceApi, (source, result), static s => s.source.TrySetResult(s.result)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(Exception)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, Exception exception)
    {
        Complete(GenericTaskCompletionSourceApi, (source, exception), static s => s.source.SetException(s.exception));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static void SetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions)
    {
        Complete(GenericTaskCompletionSourceApi, (source, exceptions), static s => s.source.SetException(s.exceptions));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(Exception)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, Exception exception) =>
        Published(
            TryComplete(
                GenericTaskCompletionSourceApi,
                (source, exception),
                static s => s.source.TrySetException(s.exception)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetException(IEnumerable&lt;Exception&gt;)</c>.</summary>
    public static bool TrySetException<TResult>(TaskCompletionSource<TResult> source, IEnumerable<Exception> exceptions) =>
        Published(
            TryComplete(
                GenericTaskCompletionSourceApi,
                (source, exceptions),
                static s => s.source.TrySetException(s.exceptions)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled()</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source)
    {
        Complete(GenericTaskCompletionSourceApi, source, static source => source.SetCanceled());
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetCanceled(CancellationToken)</c>.</summary>
    public static void SetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken)
    {
        Complete(
            GenericTaskCompletionSourceApi,
            (source, cancellationToken),
            static s => s.source.SetCanceled(s.cancellationToken));
        Published(source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled()</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source) =>
        Published(
            TryComplete(GenericTaskCompletionSourceApi, source, static source => source.TrySetCanceled()),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetCanceled(CancellationToken)</c>.</summary>
    public static bool TrySetCanceled<TResult>(TaskCompletionSource<TResult> source, CancellationToken cancellationToken) =>
        Published(
            TryComplete(
                GenericTaskCompletionSourceApi,
                (source, cancellationToken),
                static s => s.source.TrySetCanceled(s.cancellationToken)),
            source.Task);

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.SetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static void SetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        Complete(
            GenericTaskCompletionSourceApi,
            (source, completedTask),
            static s => s.source.SetFromTask(s.completedTask));
        Published(completedTask, source.Task);
    }

    /// <summary>Controlled <c>TaskCompletionSource&lt;TResult&gt;.TrySetFromTask(Task&lt;TResult&gt;)</c>.</summary>
    public static bool TrySetFromTask<TResult>(TaskCompletionSource<TResult> source, Task<TResult> completedTask)
    {
        if (!TryComplete(
            GenericTaskCompletionSourceApi,
            (source, completedTask),
            static s => s.source.TrySetFromTask(s.completedTask)))
        {
            return false;
        }

        Published(completedTask, source.Task);
        return true;
    }

    // Completes the source through the BCL member, inside a simulation as on a thread-pool thread.
    private static void Complete<TState>(
        string api,
        TState state,
        Action<TState> complete,
        [CallerMemberName] string member = "") =>
        _ = TryComplete(
            api,
            (state, complete),
            static invocation =>
            {
                invocation.complete(invocation.state);
                return true;
            },
            member);

    private static bool TryComplete<TState>(
        string api,
        TState state,
        Func<TState, bool> complete,
        [CallerMemberName] string member = "") =>
        SimulationRuntimeDispatch.RequireSimulationThreadIfActive(api, member)
            ? ThreadPoolLocation.Run(state, complete)
            : complete(state);

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
