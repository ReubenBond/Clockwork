using System.Threading;
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
/// the real API to reproduce its exact completion/exception semantics. <see cref="Run(Action)"/> and
/// its overloads queue their body as a fresh controlled operation on the coordinator (Phase 6B), so
/// the work runs on the single logical thread interleaved with everything else instead of on an
/// uncontrolled physical thread-pool thread. <see cref="Delay(int)"/> stays rejected: virtual timers
/// belong to a later phase and must never silently fall back to wall-clock time.
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
        ValidateNoNullTasks(tasks);
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
        ValidateNoNullTasks(tasks);
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

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => ControlledTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
                context,
                () => RunContinuation(() => continuationAction(antecedent), tcs)),
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

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => ControlledTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
                context,
                () => RunContinuation(() => continuationFunction(antecedent), tcs)),
            "System.Threading.Tasks.Task.ContinueWith",
            flowExecutionContext: false);
        return tcs.Task;
    }

    /// <summary>
    /// Controlled default <c>Task&lt;TResult&gt;.ContinueWith(Action&lt;Task&lt;TResult&gt;&gt;)</c>: the
    /// generic-antecedent continuation form. The continuation observes the completed antecedent (typed as
    /// <see cref="Task{TResult}"/>) and runs on the logical thread once it completes.
    /// </summary>
    /// <typeparam name="TResult">The antecedent result type.</typeparam>
    /// <param name="antecedent">The task to continue from.</param>
    /// <param name="continuationAction">The continuation to run.</param>
    /// <returns>A task representing the continuation.</returns>
    public static Task ContinueWith<TResult>(Task<TResult> antecedent, Action<Task<TResult>> continuationAction)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationAction);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return antecedent.ContinueWith(continuationAction, TaskScheduler.Current);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => ControlledTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
                context,
                () => RunContinuation(() => continuationAction(antecedent), tcs)),
            "System.Threading.Tasks.Task.ContinueWith",
            flowExecutionContext: false);
        return tcs.Task;
    }

    /// <summary>
    /// Controlled default <c>Task&lt;TResult&gt;.ContinueWith&lt;TNewResult&gt;(Func&lt;Task&lt;TResult&gt;, TNewResult&gt;)</c>:
    /// the generic-antecedent, generic-result continuation form (a generic method on a closed generic
    /// type). The call-site pass binds the shim's two type parameters declaring-type-first
    /// (<typeparamref name="TResult"/>) then method-argument (<typeparamref name="TNewResult"/>).
    /// </summary>
    /// <typeparam name="TResult">The antecedent result type.</typeparam>
    /// <typeparam name="TNewResult">The continuation result type.</typeparam>
    /// <param name="antecedent">The task to continue from.</param>
    /// <param name="continuationFunction">The continuation to run.</param>
    /// <returns>A task representing the continuation's result.</returns>
    public static Task<TNewResult> ContinueWith<TResult, TNewResult>(
        Task<TResult> antecedent,
        Func<Task<TResult>, TNewResult> continuationFunction)
    {
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationFunction);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return antecedent.ContinueWith(continuationFunction, TaskScheduler.Current);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TNewResult>();
        ControlledTaskRuntime.ScheduleContinuation(
            antecedent,
            () => ControlledTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
                context,
                () => RunContinuation(() => continuationFunction(antecedent), tcs)),
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
    /// <param name="millisecondsDelay">The delay duration.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Always thrown inside a simulation.</exception>
    public static Task Delay(int millisecondsDelay)
    {
        RejectDelayInsideSimulation();

        return Task.Delay(millisecondsDelay);
    }

    /// <summary>Rejects <c>Task.Delay(TimeSpan)</c> inside simulation and passes through otherwise.</summary>
    /// <param name="delay">The delay duration.</param>
    /// <returns>The real BCL delay task outside simulation.</returns>
    public static Task Delay(TimeSpan delay)
    {
        RejectDelayInsideSimulation();
        return Task.Delay(delay);
    }

    /// <summary>Rejects <c>Task.Delay(int, CancellationToken)</c> inside simulation and passes through otherwise.</summary>
    /// <param name="millisecondsDelay">The delay duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The real BCL delay task outside simulation.</returns>
    public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
    {
        RejectDelayInsideSimulation();
        return Task.Delay(millisecondsDelay, cancellationToken);
    }

    /// <summary>Rejects <c>Task.Delay(TimeSpan, CancellationToken)</c> inside simulation and passes through otherwise.</summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The real BCL delay task outside simulation.</returns>
    public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        RejectDelayInsideSimulation();
        return Task.Delay(delay, cancellationToken);
    }

    /// <summary>Rejects <c>Task.Delay(TimeSpan, TimeProvider)</c> inside simulation and passes through otherwise.</summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <returns>The real BCL delay task outside simulation.</returns>
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider)
    {
        RejectDelayInsideSimulation();
        return Task.Delay(delay, timeProvider);
    }

    /// <summary>Rejects <c>Task.Delay(TimeSpan, TimeProvider, CancellationToken)</c> inside simulation and passes through otherwise.</summary>
    /// <param name="delay">The delay duration.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The real BCL delay task outside simulation.</returns>
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        RejectDelayInsideSimulation();
        return Task.Delay(delay, timeProvider, cancellationToken);
    }

    /// <summary>
    /// Controlled <c>Task.Run(Action)</c>. Under simulation the work is queued as a fresh controlled
    /// operation on the coordinator - it runs on the single logical thread interleaved with all other
    /// controlled work, never on an uncontrolled physical thread-pool thread - and the returned task
    /// completes (or faults/cancels) exactly as the real <c>Task.Run</c> would. Outside a simulation it
    /// delegates to the real <see cref="Task.Run(Action)"/>.
    /// </summary>
    /// <param name="action">The work to run.</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task Run(Action action) => Run(action, CancellationToken.None);

    /// <summary>Controlled <c>Task.Run(Action, CancellationToken)</c>.</summary>
    /// <param name="action">The work to run.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task Run(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Task.Run(action, cancellationToken);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        ControlledTaskRuntime.QueueWork(
            () => ControlledTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        action();
                        tcs.TrySetResult();
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }),
            "System.Threading.Tasks.Task.Run");
        return tcs.Task;
    }

    /// <summary>Controlled <c>Task.Run&lt;TResult&gt;(Func&lt;TResult&gt;)</c>.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The work to run.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> Run<TResult>(Func<TResult> function) => Run(function, CancellationToken.None);

    /// <summary>Controlled <c>Task.Run&lt;TResult&gt;(Func&lt;TResult&gt;, CancellationToken)</c>.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The work to run.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Task.Run(function, cancellationToken);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        ControlledTaskRuntime.QueueWork(
            () => ControlledTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        tcs.TrySetResult(function());
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }),
            "System.Threading.Tasks.Task.Run");
        return tcs.Task;
    }

    /// <summary>
    /// Controlled <c>Task.Run(Func&lt;Task&gt;)</c>: the returned task completes only when the inner task
    /// the delegate produces completes (the same "unwrap" semantics as the real overload), with the inner
    /// task's own completion driven deterministically through the coordinator.
    /// </summary>
    /// <param name="function">The asynchronous work to run.</param>
    /// <returns>A task representing the unwrapped inner task.</returns>
    public static Task Run(Func<Task> function) => Run(function, CancellationToken.None);

    /// <summary>Controlled <c>Task.Run(Func&lt;Task&gt;, CancellationToken)</c> with unwrap semantics.</summary>
    /// <param name="function">The asynchronous work to run.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task representing the unwrapped inner task.</returns>
    public static Task Run(Func<Task> function, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Task.Run(function, cancellationToken);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        ControlledTaskRuntime.QueueWork(
            () => ControlledTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    Task inner;
                    try
                    {
                        inner = function();
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                        return;
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        return;
                    }

                    if (inner is null)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            "Task.Run(Func<Task>) delegate returned a null task."));
                        return;
                    }

                    ControlledTaskRuntime.ScheduleContinuation(
                        inner,
                        () => PropagateTo(inner, tcs),
                        "System.Threading.Tasks.Task.Run",
                        flowExecutionContext: false);
                }),
            "System.Threading.Tasks.Task.Run");
        return tcs.Task;
    }

    /// <summary>Controlled <c>Task.Run&lt;TResult&gt;(Func&lt;Task&lt;TResult&gt;&gt;)</c> with unwrap semantics.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The asynchronous work to run.</param>
    /// <returns>A task representing the unwrapped inner task's result.</returns>
    public static Task<TResult> Run<TResult>(Func<Task<TResult>> function) => Run(function, CancellationToken.None);

    /// <summary>Controlled <c>Task.Run&lt;TResult&gt;(Func&lt;Task&lt;TResult&gt;&gt;, CancellationToken)</c> with unwrap semantics.</summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The asynchronous work to run.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task representing the unwrapped inner task's result.</returns>
    public static Task<TResult> Run<TResult>(Func<Task<TResult>> function, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return Task.Run(function, cancellationToken);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        ControlledTaskRuntime.QueueWork(
            () => ControlledTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    Task<TResult> inner;
                    try
                    {
                        inner = function();
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                        return;
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        return;
                    }

                    if (inner is null)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            "Task.Run(Func<Task<TResult>>) delegate returned a null task."));
                        return;
                    }

                    ControlledTaskRuntime.ScheduleContinuation(
                        inner,
                        () => PropagateTo(inner, tcs),
                        "System.Threading.Tasks.Task.Run",
                        flowExecutionContext: false);
                }),
            "System.Threading.Tasks.Task.Run");
        return tcs.Task;
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

    private static void RejectDelayInsideSimulation()
    {
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.Task.Delay",
                "virtual time and timers are owned by Phase 8; using Task.Delay inside a simulation is rejected " +
                "so no overload can silently consume wall time.");
        }
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
        catch (Exception ex)
        {
            tcs.SetException(ex);
        }
    }

    /// <summary>Copies a completed antecedent's terminal status (success/fault/cancel) onto a non-generic source.</summary>
    private static void PropagateTo(Task inner, TaskCompletionSource tcs)
    {
        if (inner.IsCanceled)
        {
            tcs.TrySetCanceled(GetCancellationToken(inner));
        }
        else if (inner.IsFaulted)
        {
            tcs.TrySetException(inner.Exception!.InnerExceptions);
        }
        else
        {
            tcs.TrySetResult();
        }
    }

    /// <summary>Copies a completed antecedent's terminal status (success/fault/cancel) onto a generic source.</summary>
    private static void PropagateTo<TResult>(Task<TResult> inner, TaskCompletionSource<TResult> tcs)
    {
        if (inner.IsCanceled)
        {
            tcs.TrySetCanceled(GetCancellationToken(inner));
        }
        else if (inner.IsFaulted)
        {
            tcs.TrySetException(inner.Exception!.InnerExceptions);
        }
        else
        {
            tcs.TrySetResult(inner.Result);
        }
    }

    private static void ValidateNoNullTasks(Task[] tasks)
    {
        foreach (Task? task in tasks)
        {
            if (task is null)
            {
                throw new ArgumentException("The tasks array included at least one null element.", nameof(tasks));
            }
        }
    }

    private static CancellationToken GetCancellationToken(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            return exception.CancellationToken;
        }

        return CancellationToken.None;
    }
}
