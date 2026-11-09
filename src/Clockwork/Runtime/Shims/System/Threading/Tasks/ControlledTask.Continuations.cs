using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Shims;

namespace Clockwork.Shims.System.Threading.Tasks;

#pragma warning disable CA1068 // CancellationToken parameters mirror the BCL ContinueWith overload order.

/// <summary>
/// <para>
/// Controlled <see cref="Task.ContinueWith(Action{Task})"/> and <see cref="Task{TResult}.ContinueWith(Action{Task{TResult}})"/>:
/// every .NET 10 overload of both types, with and without state, a cancellation token, continuation options, and a
/// scheduler. The returned task is a genuine BCL continuation task, so its status, <see cref="Task.CreationOptions"/>,
/// <see cref="Task.AsyncState"/>, cancellation (including <see cref="TaskContinuationOptions.LazyCancellation"/>),
/// <c>NotOn*</c>/<c>OnlyOn*</c> filtering, and exception handling are the BCL's own. It continues a private gate
/// rather than the antecedent, so the BCL registers nothing on the antecedent: the coordinator observes the antecedent
/// complete, as the controlled awaiters do, and a controlled operation then copies the antecedent's outcome to the
/// gate, which runs the continuation inline on that operation.
/// </para>
/// <para>
/// <see cref="TaskContinuationOptions.ExecuteSynchronously"/> is a hint in the BCL and is always applied to the gate, so
/// the continuation runs on the logical thread rather than being queued to the thread pool; the continuation therefore
/// never runs inside the call that completes the antecedent, and never inside <c>ContinueWith</c> itself.
/// <see cref="TaskContinuationOptions.LongRunning"/> (which requests a dedicated thread),
/// <see cref="TaskContinuationOptions.AttachedToParent"/> (Clockwork's controlled tasks have no parent), and any
/// scheduler other than <see cref="TaskScheduler.Default"/> (explicit, or <see cref="TaskScheduler.Current"/> for the
/// overloads without a scheduler) cannot be preserved and throw <see cref="SimulationApiException"/>.
/// </para>
/// </summary>
public static partial class ControlledTask
{
    private const string ContinueWithApi = "System.Threading.Tasks.Task.ContinueWith";
    private const string GenericContinueWithApi = "System.Threading.Tasks.Task`1.ContinueWith";

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task&gt;)</c>.</summary>
    public static Task ContinueWith(Task antecedent, Action<Task> continuationAction) =>
        ContinueWith(antecedent, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task&gt;, CancellationToken)</c>.</summary>
    public static Task ContinueWith(Task antecedent, Action<Task> continuationAction, CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task&gt;, TaskScheduler)</c>.</summary>
    public static Task ContinueWith(Task antecedent, Action<Task> continuationAction, TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationAction, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task&gt;, TaskContinuationOptions)</c>.</summary>
    public static Task ContinueWith(Task antecedent, Action<Task> continuationAction, TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationAction, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task&gt;, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task ContinueWith(
        Task antecedent,
        Action<Task> continuationAction,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationAction,
            nameof(continuationAction),
            cancellationToken,
            continuationOptions,
            scheduler,
            ContinueWithApi,
            (gate, token, options) => gate.ContinueWith(_ => continuationAction(antecedent), token, options, TaskScheduler.Default));

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task, object&gt;, object)</c>.</summary>
    public static Task ContinueWith(Task antecedent, Action<Task, object?> continuationAction, object? state) =>
        ContinueWith(antecedent, continuationAction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task, object&gt;, object, CancellationToken)</c>.</summary>
    public static Task ContinueWith(
        Task antecedent,
        Action<Task, object?> continuationAction,
        object? state,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationAction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task, object&gt;, object, TaskScheduler)</c>.</summary>
    public static Task ContinueWith(
        Task antecedent,
        Action<Task, object?> continuationAction,
        object? state,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationAction, state, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task, object&gt;, object, TaskContinuationOptions)</c>.</summary>
    public static Task ContinueWith(
        Task antecedent,
        Action<Task, object?> continuationAction,
        object? state,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationAction, state, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith(Action&lt;Task, object&gt;, object, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task ContinueWith(
        Task antecedent,
        Action<Task, object?> continuationAction,
        object? state,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationAction,
            nameof(continuationAction),
            cancellationToken,
            continuationOptions,
            scheduler,
            ContinueWithApi,
            (gate, token, options) => gate.ContinueWith(
                (_, value) => continuationAction(antecedent, value),
                state,
                token,
                options,
                TaskScheduler.Default));

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, TResult&gt;)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(Task antecedent, Func<Task, TResult> continuationFunction) =>
        ContinueWith(antecedent, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, TResult&gt;, CancellationToken)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, TResult> continuationFunction,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, TResult&gt;, TaskScheduler)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, TResult> continuationFunction,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationFunction, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, TResult&gt;, TaskContinuationOptions)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, TResult> continuationFunction,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationFunction, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, TResult&gt;, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, TResult> continuationFunction,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationFunction,
            nameof(continuationFunction),
            cancellationToken,
            continuationOptions,
            scheduler,
            ContinueWithApi,
            (gate, token, options) => gate.ContinueWith(_ => continuationFunction(antecedent), token, options, TaskScheduler.Default));

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, object, TResult&gt;, object)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, object?, TResult> continuationFunction,
        object? state) =>
        ContinueWith(antecedent, continuationFunction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, object, TResult&gt;, object, CancellationToken)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, object?, TResult> continuationFunction,
        object? state,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationFunction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, object, TResult&gt;, object, TaskScheduler)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, object?, TResult> continuationFunction,
        object? state,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationFunction, state, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, object, TResult&gt;, object, TaskContinuationOptions)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, object?, TResult> continuationFunction,
        object? state,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationFunction, state, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task.ContinueWith&lt;TResult&gt;(Func&lt;Task, object, TResult&gt;, object, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task<TResult> ContinueWith<TResult>(
        Task antecedent,
        Func<Task, object?, TResult> continuationFunction,
        object? state,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationFunction,
            nameof(continuationFunction),
            cancellationToken,
            continuationOptions,
            scheduler,
            ContinueWithApi,
            (gate, token, options) => gate.ContinueWith(
                (_, value) => continuationFunction(antecedent, value),
                state,
                token,
                options,
                TaskScheduler.Default));

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    public static Task ContinueWith<TResult>(Task<TResult> antecedent, Action<Task<TResult>> continuationAction) =>
        ContinueWith(antecedent, continuationAction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;, CancellationToken)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>> continuationAction,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationAction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;, TaskScheduler)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>> continuationAction,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationAction, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;, TaskContinuationOptions)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>> continuationAction,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationAction, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>> continuationAction,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationAction,
            nameof(continuationAction),
            cancellationToken,
            continuationOptions,
            scheduler,
            GenericContinueWithApi,
            (gate, token, options) => gate.ContinueWith(_ => continuationAction(antecedent), token, options, TaskScheduler.Default));

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;, object&gt;, object)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>, object?> continuationAction,
        object? state) =>
        ContinueWith(antecedent, continuationAction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;, object&gt;, object, CancellationToken)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>, object?> continuationAction,
        object? state,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationAction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;, object&gt;, object, TaskScheduler)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>, object?> continuationAction,
        object? state,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationAction, state, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;, object&gt;, object, TaskContinuationOptions)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>, object?> continuationAction,
        object? state,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationAction, state, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;, object&gt;, object, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task ContinueWith<TResult>(
        Task<TResult> antecedent,
        Action<Task<TResult>, object?> continuationAction,
        object? state,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationAction,
            nameof(continuationAction),
            cancellationToken,
            continuationOptions,
            scheduler,
            GenericContinueWithApi,
            (gate, token, options) => gate.ContinueWith(
                (_, value) => continuationAction(antecedent, value),
                state,
                token,
                options,
                TaskScheduler.Default));

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction) =>
        ContinueWith(antecedent, continuationFunction, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;, CancellationToken)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationFunction, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;, TaskScheduler)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationFunction, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;, TaskContinuationOptions)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationFunction, default, continuationOptions, TaskScheduler.Current);

    /// <summary>
    /// Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.
    /// The call-site pass binds the type parameters declaring-type-first (<typeparamref name="TResult"/>) then
    /// method-argument (<typeparamref name="TNewResult"/>).
    /// </summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationFunction,
            nameof(continuationFunction),
            cancellationToken,
            continuationOptions,
            scheduler,
            GenericContinueWithApi,
            (gate, token, options) => gate.ContinueWith(_ => continuationFunction(antecedent), token, options, TaskScheduler.Default));

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, object, TNewResult&gt;, object)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, object?, TNewResult> continuationFunction,
        object? state) =>
        ContinueWith(antecedent, continuationFunction, state, default, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, object, TNewResult&gt;, object, CancellationToken)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, object?, TNewResult> continuationFunction,
        object? state,
        CancellationToken cancellationToken) =>
        ContinueWith(antecedent, continuationFunction, state, cancellationToken, TaskContinuationOptions.None, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, object, TNewResult&gt;, object, TaskScheduler)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, object?, TNewResult> continuationFunction,
        object? state,
        TaskScheduler scheduler) =>
        ContinueWith(antecedent, continuationFunction, state, default, TaskContinuationOptions.None, scheduler);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, object, TNewResult&gt;, object, TaskContinuationOptions)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, object?, TNewResult> continuationFunction,
        object? state,
        TaskContinuationOptions continuationOptions) =>
        ContinueWith(antecedent, continuationFunction, state, default, continuationOptions, TaskScheduler.Current);

    /// <summary>Controlled <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, object, TNewResult&gt;, object, CancellationToken, TaskContinuationOptions, TaskScheduler)</c>.</summary>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, object?, TNewResult> continuationFunction,
        object? state,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler) =>
        Continue(
            antecedent,
            continuationFunction,
            nameof(continuationFunction),
            cancellationToken,
            continuationOptions,
            scheduler,
            GenericContinueWithApi,
            (gate, token, options) => gate.ContinueWith(
                (_, value) => continuationFunction(antecedent, value),
                state,
                token,
                options,
                TaskScheduler.Default));

    private static TContinuation Continue<TContinuation>(
        Task antecedent,
        Delegate continuation,
        string continuationParameterName,
        CancellationToken cancellationToken,
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler,
        string apiName,
        Func<Task, CancellationToken, TaskContinuationOptions, TContinuation> continueGate)
        where TContinuation : Task
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(apiName);

        // The BCL's ContinueWith is an instance method: a null antecedent fails the call itself, with the runtime's
        // NullReferenceException, before any argument is validated.
        _ = antecedent.IsCompleted;

        ArgumentNullException.ThrowIfNull(continuation, continuationParameterName);
        ArgumentNullException.ThrowIfNull(scheduler);
        ValidateContinuationOptions(continuationOptions);
        RejectUnpreservableContinuation(continuationOptions, scheduler, apiName);

        ExecutionContext? context = ExecutionContext.Capture();
        var gate = new TaskCompletionSource();

        // The continuation runs inline on a controlled operation, in the execution context captured here and on a
        // fresh logical strand, so the gate continuation must not capture and restore a context of its own.
        TContinuation continuationTask;
        if (ExecutionContext.IsFlowSuppressed())
        {
            continuationTask = continueGate(gate.Task, cancellationToken, continuationOptions | TaskContinuationOptions.ExecuteSynchronously);
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                continuationTask = continueGate(gate.Task, cancellationToken, continuationOptions | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        SimulationTaskRuntime.ScheduleWhenReady(
            () => antecedent.IsCompleted || continuationTask.IsCompleted,
            () => SimulationTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
                context,
                () =>
                {
                    // A continuation canceled through its token before the antecedent completes is removed from the
                    // gate by the BCL, so nothing waits for the antecedent any longer.
                    if (!antecedent.IsCompleted)
                    {
                        return;
                    }

                    RaceSynchronization.Wait(antecedent);
                    gate.TrySetFromTask(antecedent);

                    // The gate copies the antecedent's exceptions; marking them observed keeps the copy from raising a
                    // second UnobservedTaskException for a fault that belongs to the antecedent.
                    _ = gate.Task.Exception;
                    PublishCompletion(continuationTask);
                }),
            apiName);
        return continuationTask;
    }

    /// <summary>Validates continuation options exactly as the BCL's <c>ContinueWith</c> does.</summary>
    private static void ValidateContinuationOptions(TaskContinuationOptions continuationOptions)
    {
        const TaskContinuationOptions NotOnAnything =
            TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted | TaskContinuationOptions.NotOnRanToCompletion;
        const TaskContinuationOptions CreationOptionsMask =
            TaskContinuationOptions.PreferFairness | TaskContinuationOptions.LongRunning | TaskContinuationOptions.DenyChildAttach |
            TaskContinuationOptions.HideScheduler | TaskContinuationOptions.AttachedToParent |
            TaskContinuationOptions.RunContinuationsAsynchronously;
        const TaskContinuationOptions IllegalMask = TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.LongRunning;

        if ((continuationOptions & IllegalMask) == IllegalMask)
        {
            throw new ArgumentOutOfRangeException(
                nameof(continuationOptions),
                "The specified TaskContinuationOptions combined LongRunning and ExecuteSynchronously.  Synchronous continuations should not be long running.");
        }

        if ((continuationOptions & ~(CreationOptionsMask | NotOnAnything | TaskContinuationOptions.LazyCancellation |
            TaskContinuationOptions.ExecuteSynchronously)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(continuationOptions));
        }

        if ((continuationOptions & NotOnAnything) == NotOnAnything)
        {
            throw new ArgumentOutOfRangeException(
                nameof(continuationOptions),
                "The specified TaskContinuationOptions excluded all continuation kinds.");
        }
    }

    private static void RejectUnpreservableContinuation(
        TaskContinuationOptions continuationOptions,
        TaskScheduler scheduler,
        string apiName)
    {
        if (!ReferenceEquals(scheduler, TaskScheduler.Default))
        {
            throw new SimulationApiException(
                SimulationApiCategory.Task,
                apiName,
                "a custom TaskScheduler (including a non-default TaskScheduler.Current) is not supported inside a " +
                "simulation because executing it would escape Clockwork's controlled logical strand. Use TaskScheduler.Default.");
        }

        if ((continuationOptions & TaskContinuationOptions.LongRunning) != 0)
        {
            throw new SimulationApiException(
                SimulationApiCategory.Task,
                apiName,
                "TaskContinuationOptions.LongRunning requests a dedicated thread, which a continuation on the " +
                "simulation's logical thread cannot preserve.");
        }

        if ((continuationOptions & TaskContinuationOptions.AttachedToParent) != 0)
        {
            throw new SimulationApiException(
                SimulationApiCategory.Task,
                apiName,
                "TaskContinuationOptions.AttachedToParent attaches the continuation to a parent task, and Clockwork's " +
                "controlled tasks do not model parent and child tasks.");
        }
    }
}

#pragma warning restore CA1068
