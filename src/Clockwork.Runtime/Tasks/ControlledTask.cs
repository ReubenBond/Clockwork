using System.Threading.Tasks;
using Clockwork.Runtime.Tasks.CompilerServices;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// Static shims for the <see cref="Task"/> and <see cref="Task{TResult}"/> surface that ordinary
/// application code calls directly (as opposed to the compiler-generated builders/awaiters). The
/// rewriter redirects each supported call site here; instance members are exposed as static methods
/// whose first parameter is the receiver, matching Clockwork's <c>RedirectCall</c> convention.
/// </para>
/// <para>
/// The guiding principle is "delegate for correctness, control for scheduling". Combinators
/// (<see cref="WhenAll(Task[])"/>, <see cref="WhenAny(Task[])"/>) delegate to the real BCL: their
/// completion is driven entirely by their antecedents, which complete on the simulation's single
/// logical thread, so awaiting the result through a controlled awaiter is already deterministic.
/// Synchronous waits (<see cref="Wait"/>, <see cref="Result{TResult}"/>, <see cref="WaitAll"/>,
/// <see cref="WaitAny"/>) pump the coordinator instead of blocking a physical thread, then delegate to
/// the real API to reproduce its exact completion/exception semantics. Timer- and thread-pool-based
/// APIs (<see cref="Delay"/>, <see cref="Run"/>) are explicitly rejected: they belong to later phases
/// and must never silently fall back to wall-clock time or an uncontrolled thread.
/// </para>
/// </summary>
public static class ControlledTask
{
    /// <summary>Controlled <c>Task.Yield()</c>: returns an awaitable that always suspends and resumes via the coordinator.</summary>
    /// <returns>A <see cref="ControlledYieldAwaitable"/>.</returns>
    public static ControlledYieldAwaitable Yield() => default;

    /// <summary>Controlled <c>Task.WhenAll(Task[])</c>.</summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(params Task[] tasks) => Task.WhenAll(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAll(ReadOnlySpan&lt;Task&gt;)</c>: the .NET 9+ params-span overload that a
    /// two-or-more argument <c>Task.WhenAll(a, b, ...)</c> call binds to.
    /// </summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(params ReadOnlySpan<Task> tasks) => Task.WhenAll(tasks);

    /// <summary>Controlled <c>Task.WhenAll(IEnumerable&lt;Task&gt;)</c>.</summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(IEnumerable<Task> tasks) => Task.WhenAll(tasks);

    /// <summary>Controlled <c>Task.WhenAll&lt;TResult&gt;(Task&lt;TResult&gt;[])</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks) => Task.WhenAll(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAll&lt;TResult&gt;(ReadOnlySpan&lt;Task&lt;TResult&gt;&gt;)</c>: the .NET 9+
    /// params-span overload that a two-or-more argument <c>Task.WhenAll(a, b, ...)</c> of typed tasks
    /// binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(params ReadOnlySpan<Task<TResult>> tasks) => Task.WhenAll(tasks);

    /// <summary>Controlled <c>Task.WhenAll&lt;TResult&gt;(IEnumerable&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks) => Task.WhenAll(tasks);

    /// <summary>Controlled <c>Task.WhenAny(Task[])</c>. First-completer order is deterministic because tasks complete one loop step at a time.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(params Task[] tasks) => Task.WhenAny(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAny(ReadOnlySpan&lt;Task&gt;)</c>: the .NET 9+ params-span overload that a
    /// three-or-more argument <c>Task.WhenAny(a, b, c, ...)</c> call binds to.
    /// </summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(params ReadOnlySpan<Task> tasks) => Task.WhenAny(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAny(Task, Task)</c>: the two-argument overload that <c>Task.WhenAny(a, b)</c>
    /// binds to.
    /// </summary>
    /// <param name="task1">The first candidate task.</param>
    /// <param name="task2">The second candidate task.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(Task task1, Task task2) => Task.WhenAny(task1, task2);

    /// <summary>Controlled <c>Task.WhenAny(IEnumerable&lt;Task&gt;)</c>.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(IEnumerable<Task> tasks) => Task.WhenAny(tasks);

    /// <summary>Controlled <c>Task.WhenAny&lt;TResult&gt;(Task&lt;TResult&gt;[])</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks) => Task.WhenAny(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAny&lt;TResult&gt;(ReadOnlySpan&lt;Task&lt;TResult&gt;&gt;)</c>: the .NET 9+
    /// params-span overload that a three-or-more argument typed <c>Task.WhenAny(a, b, c, ...)</c> binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(params ReadOnlySpan<Task<TResult>> tasks) => Task.WhenAny(tasks);

    /// <summary>
    /// Controlled <c>Task.WhenAny&lt;TResult&gt;(Task&lt;TResult&gt;, Task&lt;TResult&gt;)</c>: the
    /// two-argument overload that a typed <c>Task.WhenAny(a, b)</c> binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="task1">The first candidate task.</param>
    /// <param name="task2">The second candidate task.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(Task<TResult> task1, Task<TResult> task2) => Task.WhenAny(task1, task2);

    /// <summary>Controlled <c>Task.WhenAny&lt;TResult&gt;(IEnumerable&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks) => Task.WhenAny(tasks);

    /// <summary>
    /// Controlled <c>TaskExtensions.Unwrap(this Task&lt;Task&gt;)</c>. Like the combinators this delegates
    /// to the real BCL: the outer and inner tasks both complete on the simulation's single logical
    /// thread, so the unwrapped proxy's completion is already driven deterministically and awaiting it
    /// through a controlled awaiter needs no further scheduling.
    /// </summary>
    /// <param name="task">The nested task to unwrap.</param>
    /// <returns>A task representing the completion of the inner task.</returns>
    public static Task Unwrap(Task<Task> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Unwrap();
    }

    /// <summary>Controlled <c>TaskExtensions.Unwrap&lt;TResult&gt;(this Task&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The inner task result type.</typeparam>
    /// <param name="task">The nested task to unwrap.</param>
    /// <returns>A task representing the completion (and result) of the inner task.</returns>
    public static Task<TResult> Unwrap<TResult>(Task<Task<TResult>> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.Unwrap();
    }

    /// <summary>
    /// Controlled instance <c>task.Wait()</c>. Pumps the coordinator until the task completes (never
    /// blocking a physical thread) then delegates to the real <see cref="Task.Wait()"/> so the exact
    /// <see cref="AggregateException"/> semantics are preserved.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    public static void Wait(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            ControlledTaskRuntime.DrainUntilCompleted(task, "System.Threading.Tasks.Task.Wait");
        }

        task.Wait();
    }

    /// <summary>
    /// Controlled instance <c>task.Result</c> getter. Pumps the coordinator until the task completes then
    /// returns the real <see cref="Task{TResult}.Result"/> (preserving <see cref="AggregateException"/>
    /// semantics on fault/cancel).
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="task">The task to read the result of.</param>
    /// <returns>The task's result.</returns>
    public static TResult Result<TResult>(Task<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            ControlledTaskRuntime.DrainUntilCompleted(task, "System.Threading.Tasks.Task`1.get_Result");
        }

        return task.Result;
    }

    /// <summary>Controlled <c>Task.WaitAll(Task[])</c>: pumps until all complete, then delegates.</summary>
    /// <param name="tasks">The tasks to wait for.</param>
    public static void WaitAll(params Task[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            ControlledTaskRuntime.DrainUntil(() => AllCompleted(tasks), "System.Threading.Tasks.Task.WaitAll");
        }

        Task.WaitAll(tasks);
    }

    /// <summary>Controlled <c>Task.WaitAny(Task[])</c>: pumps until at least one completes, then delegates.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>The index of the first completed task.</returns>
    public static int WaitAny(params Task[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            ControlledTaskRuntime.DrainUntil(() => AnyCompleted(tasks), "System.Threading.Tasks.Task.WaitAny");
        }

        return Task.WaitAny(tasks);
    }

    /// <summary>
    /// Controlled default <c>task.ContinueWith(Action&lt;Task&gt;)</c>. The continuation is registered on
    /// the coordinator and runs once the antecedent completes (regardless of its final status), on the
    /// logical thread; the returned task represents the continuation's own completion.
    /// </summary>
    /// <param name="antecedent">The task to continue from.</param>
    /// <param name="continuationAction">The continuation to run.</param>
    /// <returns>A task representing the continuation.</returns>
    public static Task ContinueWith(Task antecedent, Action<Task> continuationAction)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationAction);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return antecedent.ContinueWith(continuationAction, TaskScheduler.Current);
        }

        var tcs = new TaskCompletionSource();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => RunContinuation(() => continuationAction(antecedent), tcs),
            "System.Threading.Tasks.Task.ContinueWith",
            flowExecutionContext: false);
        return tcs.Task;
    }

    /// <summary>
    /// Controlled default <c>task.ContinueWith(Func&lt;Task, TResult&gt;)</c>.
    /// </summary>
    /// <typeparam name="TResult">The continuation result type.</typeparam>
    /// <param name="antecedent">The task to continue from.</param>
    /// <param name="continuationFunction">The continuation to run.</param>
    /// <returns>A task representing the continuation's result.</returns>
    public static Task<TResult> ContinueWith<TResult>(Task antecedent, Func<Task, TResult> continuationFunction)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationFunction);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return antecedent.ContinueWith(continuationFunction, TaskScheduler.Current);
        }

        var tcs = new TaskCompletionSource<TResult>();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => RunContinuation(() => continuationFunction(antecedent), tcs),
            "System.Threading.Tasks.Task.ContinueWith",
            flowExecutionContext: false);
        return tcs.Task;
    }

    /// <summary>
    /// Controlled <c>task.RunSynchronously()</c>: delegates to the real API, which runs the task's
    /// delegate inline on the calling (logical) thread - already deterministic.
    /// </summary>
    /// <param name="task">The task to run.</param>
    public static void RunSynchronously(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.RunSynchronously();
    }

    /// <summary>
    /// Rejects <c>Task.Delay</c>: virtual-timer support is owned by a later phase. Rejecting here prevents
    /// a rewritten assembly from silently using wall-clock time inside a simulation.
    /// </summary>
    /// <param name="millisecondsDelay">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Always thrown inside a simulation.</exception>
    public static Task Delay(int millisecondsDelay)
    {
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.Task.Delay",
                "virtual time and timers are owned by a later phase; Phase 6A refuses to model a delay with " +
                "wall-clock time. Use controlled timers when that phase lands.");
        }

        return Task.Delay(millisecondsDelay);
    }

    /// <summary>
    /// Rejects <c>Task.Run</c>: thread-pool scheduling is owned by the threading phase. Rejecting here
    /// prevents a rewritten assembly from silently offloading work to an uncontrolled physical thread.
    /// </summary>
    /// <param name="action">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Always thrown inside a simulation.</exception>
    public static Task Run(Action action)
    {
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.Task.Run",
                "thread-pool scheduling is owned by the threading phase; Phase 6A refuses to offload work to " +
                "an uncontrolled physical thread. Await the work directly, or use a controlled task instead.");
        }

        return Task.Run(action);
    }

    private static bool AllCompleted(Task[] tasks)
    {
        foreach (var task in tasks)
        {
            if (!task.IsCompleted)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AnyCompleted(Task[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task.IsCompleted)
            {
                return true;
            }
        }

        return tasks.Length == 0;
    }

    private static void RunContinuation(Action body, TaskCompletionSource tcs)
    {
        try
        {
            body();
            tcs.SetResult();
        }
        catch (OperationCanceledException oce)
        {
            tcs.SetCanceled(oce.CancellationToken);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }

    private static void RunContinuation<TResult>(Func<TResult> body, TaskCompletionSource<TResult> tcs)
    {
        try
        {
            tcs.SetResult(body());
        }
        catch (OperationCanceledException oce)
        {
            tcs.SetCanceled(oce.CancellationToken);
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }
}
