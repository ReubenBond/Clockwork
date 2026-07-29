using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Shims;
using Clockwork.Shims.System.Runtime.CompilerServices;
using Clockwork.Runtime.Threading;

namespace Clockwork.Shims.System.Threading.Tasks;

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
/// its overloads queue their body as a fresh controlled operation on the coordinator, so
/// the work runs on the single logical thread interleaved with everything else instead of on an
/// uncontrolled physical thread-pool thread. Delay and timeout APIs register deterministic virtual
/// deadlines on the same coordinator and never consume wall-clock time.
/// </para>
/// </summary>
public static class ControlledTask
{
    /// <summary>Controlled <c>Task.Yield()</c>: returns an awaitable that always suspends and resumes via the coordinator.</summary>
    /// <returns>A <see cref="ControlledYieldAwaitable"/>.</returns>
    public static ControlledYieldAwaitable Yield() =>
        (SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Yield"), default(ControlledYieldAwaitable)).Item2;

    /// <summary>Controlled <c>Task.WhenAll(Task[])</c>.</summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(params Task[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        return TrackAll(Task.WhenAll(tasks), tasks);
    }

    /// <summary>
    /// Controlled <c>Task.WhenAll(ReadOnlySpan&lt;Task&gt;)</c>: the .NET 9+ params-span overload that a
    /// two-or-more argument <c>Task.WhenAll(a, b, ...)</c> call binds to.
    /// </summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(params ReadOnlySpan<Task> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        Task[] dependencies = tasks.ToArray();
        return TrackAll(Task.WhenAll(dependencies), dependencies);
    }

    /// <summary>Controlled <c>Task.WhenAll(IEnumerable&lt;Task&gt;)</c>.</summary>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task that completes when all <paramref name="tasks"/> complete.</returns>
    public static Task WhenAll(IEnumerable<Task> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        Task[] dependencies = tasks.ToArray();
        return TrackAll(Task.WhenAll(dependencies), dependencies);
    }

    /// <summary>Controlled <c>Task.WhenAll&lt;TResult&gt;(Task&lt;TResult&gt;[])</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(params Task<TResult>[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        return TrackAll(Task.WhenAll(tasks), tasks);
    }

    /// <summary>
    /// Controlled <c>Task.WhenAll&lt;TResult&gt;(ReadOnlySpan&lt;Task&lt;TResult&gt;&gt;)</c>: the .NET 9+
    /// params-span overload that a two-or-more argument <c>Task.WhenAll(a, b, ...)</c> of typed tasks
    /// binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(params ReadOnlySpan<Task<TResult>> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        Task<TResult>[] dependencies = tasks.ToArray();
        return TrackAll(Task.WhenAll(dependencies), dependencies);
    }

    /// <summary>Controlled <c>Task.WhenAll&lt;TResult&gt;(IEnumerable&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The tasks to await.</param>
    /// <returns>A task with the ordered results once all complete.</returns>
    public static Task<TResult[]> WhenAll<TResult>(IEnumerable<Task<TResult>> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAll");
        Task<TResult>[] dependencies = tasks.ToArray();
        return TrackAll(Task.WhenAll(dependencies), dependencies);
    }

    /// <summary>Controlled <c>Task.WhenAny(Task[])</c>. First-completer order is deterministic because tasks complete one loop step at a time.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(params Task[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

    /// <summary>
    /// Controlled <c>Task.WhenAny(ReadOnlySpan&lt;Task&gt;)</c>: the .NET 9+ params-span overload that a
    /// three-or-more argument <c>Task.WhenAny(a, b, c, ...)</c> call binds to.
    /// </summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(params ReadOnlySpan<Task> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

    /// <summary>
    /// Controlled <c>Task.WhenAny(Task, Task)</c>: the two-argument overload that <c>Task.WhenAny(a, b)</c>
    /// binds to.
    /// </summary>
    /// <param name="task1">The first candidate task.</param>
    /// <param name="task2">The second candidate task.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(Task task1, Task task2)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(task1, task2));
    }

    /// <summary>Controlled <c>Task.WhenAny(IEnumerable&lt;Task&gt;)</c>.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task> WhenAny(IEnumerable<Task> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

    /// <summary>Controlled <c>Task.WhenAny&lt;TResult&gt;(Task&lt;TResult&gt;[])</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(params Task<TResult>[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

    /// <summary>
    /// Controlled <c>Task.WhenAny&lt;TResult&gt;(ReadOnlySpan&lt;Task&lt;TResult&gt;&gt;)</c>: the .NET 9+
    /// params-span overload that a three-or-more argument typed <c>Task.WhenAny(a, b, c, ...)</c> binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(params ReadOnlySpan<Task<TResult>> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

    /// <summary>
    /// Controlled <c>Task.WhenAny&lt;TResult&gt;(Task&lt;TResult&gt;, Task&lt;TResult&gt;)</c>: the
    /// two-argument overload that a typed <c>Task.WhenAny(a, b)</c> binds to.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="task1">The first candidate task.</param>
    /// <param name="task2">The second candidate task.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(Task<TResult> task1, Task<TResult> task2)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(task1, task2));
    }

    /// <summary>Controlled <c>Task.WhenAny&lt;TResult&gt;(IEnumerable&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>A task whose result is the first task to complete.</returns>
    public static Task<Task<TResult>> WhenAny<TResult>(IEnumerable<Task<TResult>> tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WhenAny");
        return TrackWinner(Task.WhenAny(tasks));
    }

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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.TaskExtensions.Unwrap");
        ArgumentNullException.ThrowIfNull(task);
        Task proxy = task.Unwrap();
        return RaceTaskDependencies.Register(
            proxy,
            () => task.Status == TaskStatus.RanToCompletion
                ? [task, task.Result]
                : [task]);
    }

    /// <summary>Controlled <c>TaskExtensions.Unwrap&lt;TResult&gt;(this Task&lt;Task&lt;TResult&gt;&gt;)</c>.</summary>
    /// <typeparam name="TResult">The inner task result type.</typeparam>
    /// <param name="task">The nested task to unwrap.</param>
    /// <returns>A task representing the completion (and result) of the inner task.</returns>
    public static Task<TResult> Unwrap<TResult>(Task<Task<TResult>> task)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.TaskExtensions.Unwrap");
        ArgumentNullException.ThrowIfNull(task);
        Task<TResult> proxy = task.Unwrap();
        return RaceTaskDependencies.Register(
            proxy,
            () => task.Status == TaskStatus.RanToCompletion
                ? [task, task.Result]
                : [task]);
    }

    /// <summary>
    /// Controlled instance <c>task.Wait()</c>. Pumps the coordinator until the task completes (never
    /// blocking a physical thread) then delegates to the real <see cref="Task.Wait()"/> so the exact
    /// <see cref="AggregateException"/> semantics are preserved.
    /// </summary>
    /// <param name="task">The task to wait for.</param>
    public static void Wait(Task task)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Wait");
        ArgumentNullException.ThrowIfNull(task);
        SimulationTaskRuntime.DrainUntilCompleted(
            task,
            "System.Threading.Tasks.Task.Wait",
            CancellationToken.None);
        RaceSynchronization.Wait(task);
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.get_Result");
        ArgumentNullException.ThrowIfNull(task);
        SimulationTaskRuntime.DrainUntilCompleted(
            task,
            "System.Threading.Tasks.Task`1.get_Result",
            CancellationToken.None);
        RaceSynchronization.Wait(task);
        return task.Result;
    }

    /// <summary>Controlled <c>Task.WaitAll(Task[])</c>: pumps until all complete, then delegates.</summary>
    /// <param name="tasks">The tasks to wait for.</param>
    public static void WaitAll(params Task[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAll");
        ArgumentNullException.ThrowIfNull(tasks);
        ValidateNoNullTasks(tasks);
        SimulationTaskRuntime.DrainUntil(
            () => AllCompleted(tasks),
            "System.Threading.Tasks.Task.WaitAll",
            CancellationToken.None);
        foreach (Task task in tasks)
        {
            RaceSynchronization.Wait(task);
        }

        Task.WaitAll(tasks);
    }

    /// <summary>Controlled <c>Task.WaitAny(Task[])</c>: pumps until at least one completes, then delegates.</summary>
    /// <param name="tasks">The candidate tasks.</param>
    /// <returns>The index of the first completed task.</returns>
    public static int WaitAny(params Task[] tasks)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAny");
        ArgumentNullException.ThrowIfNull(tasks);
        ValidateNoNullTasks(tasks);
        SimulationTaskRuntime.DrainUntil(
            () => AnyCompleted(tasks),
            "System.Threading.Tasks.Task.WaitAny",
            CancellationToken.None);
        int winner = Task.WaitAny(tasks);
        RaceSynchronization.Wait(tasks[winner]);
        return winner;
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.ContinueWith");
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationAction);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        SimulationTaskRuntime.ScheduleContinuation(
            antecedent,
            () => SimulationTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.ContinueWith");
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationFunction);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        SimulationTaskRuntime.ScheduleContinuation(
            antecedent,
            () => SimulationTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.ContinueWith");
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationAction);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        SimulationTaskRuntime.ScheduleContinuation(
            antecedent,
            () => SimulationTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.ContinueWith");
        ArgumentNullException.ThrowIfNull(antecedent);
        ArgumentNullException.ThrowIfNull(continuationFunction);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TNewResult>();
        SimulationTaskRuntime.ScheduleContinuation(
            antecedent,
            () => SimulationTaskRuntime.RunWithCapturedExecutionContextAsNewStrand(
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.RunSynchronously");
        ArgumentNullException.ThrowIfNull(task);
        task.RunSynchronously();
    }

    /// <summary>Returns a task completed after the requested virtual delay.</summary>
    public static Task Delay(int millisecondsDelay)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        return DelayCore(ValidateMillisecondsDelay(millisecondsDelay), CancellationToken.None);
    }

    /// <summary>Returns a task completed after the requested virtual delay.</summary>
    public static Task Delay(TimeSpan delay)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        return DelayCore(ValidateTimerTimeSpan(delay, nameof(delay)), CancellationToken.None);
    }

    /// <summary>Returns a cancellable task completed after the requested virtual delay.</summary>
    public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        return DelayCore(ValidateMillisecondsDelay(millisecondsDelay), cancellationToken);
    }

    /// <summary>Returns a cancellable task completed after the requested virtual delay.</summary>
    public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        return DelayCore(ValidateTimerTimeSpan(delay, nameof(delay)), cancellationToken);
    }

    /// <summary>Returns a task completed after a virtual delay interpreted by a controlled provider.</summary>
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(delay, nameof(delay));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task.Delay");
        return DelayCore(validated, CancellationToken.None);
    }

    /// <summary>Returns a cancellable task completed after a virtual delay interpreted by a controlled provider.</summary>
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Delay");
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(delay, nameof(delay));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task.Delay");
        return DelayCore(validated, cancellationToken);
    }

    /// <summary>Waits for a task or cancellation without escaping the controlled scheduler.</summary>
    public static Task WaitAsync(Task task, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>Waits for a task or virtual timeout.</summary>
    public static Task WaitAsync(Task task, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, ValidateTimerTimeSpan(timeout, nameof(timeout)), CancellationToken.None);
    }

    /// <summary>Waits for a task or virtual timeout interpreted by a controlled provider.</summary>
    public static Task WaitAsync(Task task, TimeSpan timeout, TimeProvider timeProvider)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(timeout, nameof(timeout));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task.WaitAsync");
        return WaitAsyncCore(task, validated, CancellationToken.None);
    }

    /// <summary>Waits for a task, virtual timeout, or cancellation.</summary>
    public static Task WaitAsync(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, ValidateTimerTimeSpan(timeout, nameof(timeout)), cancellationToken);
    }

    /// <summary>Waits for a task, controlled-provider timeout, or cancellation.</summary>
    public static Task WaitAsync(
        Task task,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(timeout, nameof(timeout));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task.WaitAsync");
        return WaitAsyncCore(task, validated, cancellationToken);
    }

    /// <summary>Waits for a generic task or cancellation.</summary>
    public static Task<TResult> WaitAsync<TResult>(Task<TResult> task, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>Waits for a generic task or virtual timeout.</summary>
    public static Task<TResult> WaitAsync<TResult>(Task<TResult> task, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, ValidateTimerTimeSpan(timeout, nameof(timeout)), CancellationToken.None);
    }

    /// <summary>Waits for a generic task or controlled-provider timeout.</summary>
    public static Task<TResult> WaitAsync<TResult>(
        Task<TResult> task,
        TimeSpan timeout,
        TimeProvider timeProvider)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(timeout, nameof(timeout));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task`1.WaitAsync");
        return WaitAsyncCore(task, validated, CancellationToken.None);
    }

    /// <summary>Waits for a generic task, virtual timeout, or cancellation.</summary>
    public static Task<TResult> WaitAsync<TResult>(
        Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        return WaitAsyncCore(task, ValidateTimerTimeSpan(timeout, nameof(timeout)), cancellationToken);
    }

    /// <summary>Waits for a generic task, controlled-provider timeout, or cancellation.</summary>
    public static Task<TResult> WaitAsync<TResult>(
        Task<TResult> task,
        TimeSpan timeout,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task`1.WaitAsync");
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeSpan validated = ValidateTimerTimeSpan(timeout, nameof(timeout));
        ControlledTimeProvider.ValidateProvider(timeProvider, "System.Threading.Tasks.Task`1.WaitAsync");
        return WaitAsyncCore(task, validated, cancellationToken);
    }

    /// <summary>
    /// Controlled <c>Task.Run(Action)</c>. Under simulation the work is queued as a fresh controlled
    /// operation on the coordinator - it runs on the single logical thread interleaved with all other
    /// controlled work, never on an uncontrolled physical thread-pool thread - and the returned task
    /// completes (or faults/cancels) exactly as the real <c>Task.Run</c> would.
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Run");
        ArgumentNullException.ThrowIfNull(action);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        SimulationTaskRuntime.QueueWork(
            () => SimulationTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    try
                    {
                        action();
                        tcs.TrySetResult();
                        PublishCompletion(tcs.Task);
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                        PublishCompletion(tcs.Task);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        PublishCompletion(tcs.Task);
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Run");
        ArgumentNullException.ThrowIfNull(function);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        SimulationTaskRuntime.QueueWork(
            () => SimulationTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    try
                    {
                        tcs.TrySetResult(function());
                        PublishCompletion(tcs.Task);
                    }
                    catch (OperationCanceledException oce)
                    {
                        tcs.TrySetCanceled(oce.CancellationToken);
                        PublishCompletion(tcs.Task);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        PublishCompletion(tcs.Task);
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Run");
        ArgumentNullException.ThrowIfNull(function);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource();
        SimulationTaskRuntime.QueueWork(
            () => SimulationTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        PublishCompletion(tcs.Task);
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
                        PublishCompletion(tcs.Task);
                        return;
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    if (inner is null)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            "Task.Run(Func<Task>) delegate returned a null task."));
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    SimulationTaskRuntime.ScheduleContinuation(
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Tasks.Task.Run");
        ArgumentNullException.ThrowIfNull(function);
        ExecutionContext? context = ExecutionContext.Capture();
        var tcs = new TaskCompletionSource<TResult>();
        SimulationTaskRuntime.QueueWork(
            () => SimulationTaskRuntime.RunWithCapturedExecutionContext(
                context,
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        PublishCompletion(tcs.Task);
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
                        PublishCompletion(tcs.Task);
                        return;
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    if (inner is null)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            "Task.Run(Func<Task<TResult>>) delegate returned a null task."));
                        PublishCompletion(tcs.Task);
                        return;
                    }

                    SimulationTaskRuntime.ScheduleContinuation(
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

    private static Task DelayCore(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (delay == TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return new SimulationDelayPromise(delay, cancellationToken).Task;
    }

    private static Task WaitAsyncCore(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled))
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            return Task.FromException(new TimeoutException());
        }

        var completion = new TaskCompletionSource();
        var race = new SimulationTaskCompletionRace(
            task,
            timeout,
            () => PropagateTo(task, completion),
            () => completion.TrySetException(new TimeoutException()),
            () => completion.TrySetCanceled(cancellationToken),
            cancellationToken);
        race.Start();
        return completion.Task;
    }

    private static Task<TResult> WaitAsyncCore<TResult>(
        Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (task.IsCompleted || (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled))
        {
            return task;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TResult>(cancellationToken);
        }

        if (timeout == TimeSpan.Zero)
        {
            return Task.FromException<TResult>(new TimeoutException());
        }

        var completion = new TaskCompletionSource<TResult>();
        var race = new SimulationTaskCompletionRace(
            task,
            timeout,
            () => PropagateTo(task, completion),
            () => completion.TrySetException(new TimeoutException()),
            () => completion.TrySetCanceled(cancellationToken),
            cancellationToken);
        race.Start();
        return completion.Task;
    }

    private static TimeSpan ValidateMillisecondsDelay(int millisecondsDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsDelay, -1);
        return millisecondsDelay == Timeout.Infinite
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromMilliseconds(millisecondsDelay);
    }

    private static TimeSpan ValidateTimerTimeSpan(TimeSpan timeout, string parameterName)
    {
        const uint maxSupportedTimeout = 0xfffffffe;
        long milliseconds = (long)timeout.TotalMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, -1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, maxSupportedTimeout, parameterName);
        return milliseconds == Timeout.Infinite
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromMilliseconds(milliseconds);
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
        finally
        {
            PublishCompletion(tcs.Task);
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
        finally
        {
            PublishCompletion(tcs.Task);
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

        PublishCompletion(tcs.Task);
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

        PublishCompletion(tcs.Task);
    }

    private static void PublishCompletion(Task task)
    {
        if (task.IsCompleted)
        {
            RaceSynchronization.Signal(task);
        }
    }

    private static TTask TrackAll<TTask>(TTask proxy, IEnumerable<Task> dependencies)
        where TTask : Task
    {
        Task[] captured = dependencies.ToArray();
        return RaceTaskDependencies.Register(proxy, () => captured);
    }

    private static Task<Task> TrackWinner(Task<Task> proxy) =>
        RaceTaskDependencies.Register(proxy, () => [proxy.GetAwaiter().GetResult()]);

    private static Task<Task<TResult>> TrackWinner<TResult>(Task<Task<TResult>> proxy) =>
        RaceTaskDependencies.Register(proxy, () => [proxy.GetAwaiter().GetResult()]);

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
