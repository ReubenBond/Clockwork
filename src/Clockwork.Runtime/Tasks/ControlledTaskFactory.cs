using System.Threading;
using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks;

#pragma warning disable CA1068 // Signatures intentionally mirror the BCL TaskFactory overload order.

/// <summary>
/// <para>
/// Static shims for the <see cref="TaskFactory"/> and <see cref="TaskFactory{TResult}"/> surface. The
/// rewriter redirects the supported call sites here; instance members are exposed as static methods
/// whose first parameter is the receiver, matching Clockwork's <c>RedirectCall</c> convention.
/// </para>
/// <para>
/// <see cref="TaskFactory.StartNew(System.Action)"/> and its counterparts schedule work onto a
/// <see cref="TaskScheduler"/> - by default the thread pool. Phase 6B controls that scheduling by
/// queuing the delegate body as a fresh controlled operation on the simulation coordinator (exactly as
/// <see cref="ControlledTask.Run(System.Action)"/> does), so the work runs deterministically on the
/// single logical thread with the factory's (or the call's) cancellation token honoured. The
/// options that change scheduling or parent/continuation semantics and non-default task schedulers are
/// rejected under simulation rather than silently ignored. Every combination runs unchanged outside a simulation.
/// </para>
/// </summary>
public static class ControlledTaskFactory
{
    /// <summary>Controlled <c>TaskFactory.StartNew(Action)</c>.</summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task StartNew(TaskFactory factory, Action action)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action);
        }

        return StartNewCore(
            action,
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew(Action, CancellationToken)</c>.</summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task StartNew(TaskFactory factory, Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, cancellationToken);
        }

        return StartNewCore(
            action,
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew(Action, TaskCreationOptions)</c>.</summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.AttachedToParent"/> is rejected inside a simulation).</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task StartNew(TaskFactory factory, Action action, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, creationOptions);
        }

        return StartNewCore(
            action,
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled full-scheduler <c>TaskFactory.StartNew(Action, ...)</c> overload.</summary>
    public static Task StartNew(
        TaskFactory factory,
        Action action,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(action, cancellationToken, creationOptions, scheduler);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew(Action&lt;object&gt;, object)</c>.</summary>
    public static Task StartNew(TaskFactory factory, Action<object?> action, object? state)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, state);
        }

        return StartNewCore(
            () => action(state),
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory.StartNew</c> overload.</summary>
    public static Task StartNew(
        TaskFactory factory,
        Action<object?> action,
        object? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, state, cancellationToken);
        }

        return StartNewCore(
            () => action(state),
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory.StartNew</c> overload.</summary>
    public static Task StartNew(
        TaskFactory factory,
        Action<object?> action,
        object? state,
        TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, state, creationOptions);
        }

        return StartNewCore(
            () => action(state),
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled full-scheduler state-carrying <c>TaskFactory.StartNew</c> overload.</summary>
    public static Task StartNew(
        TaskFactory factory,
        Action<object?> action,
        object? state,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, state, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(
            () => action(state),
            cancellationToken,
            creationOptions,
            scheduler,
            state);
    }

    private static Task StartNewCore(
        Action action,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler,
        object? asyncState = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(scheduler);
        RejectUnsupportedOptions(creationOptions);
        RejectUnsupportedScheduler(scheduler);
        var tcs = new TaskCompletionSource(asyncState);
        ControlledTaskRuntime.QueueWork(
            () => RunAction(action, tcs, cancellationToken),
            "System.Threading.Tasks.TaskFactory.StartNew");
        return tcs.Task;
    }

    /// <summary>Controlled <c>TaskFactory.StartNew&lt;TResult&gt;(Func&lt;TResult&gt;)</c> - the common <c>Task.Factory.StartNew(() =&gt; ...)</c> form.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory factory, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function);
        }

        return StartNewCore(
            function,
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew&lt;TResult&gt;(Func&lt;TResult&gt;, CancellationToken)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory factory, Func<TResult> function, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken);
        }

        return StartNewCore(
            function,
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew&lt;TResult&gt;(Func&lt;TResult&gt;, TaskCreationOptions)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.AttachedToParent"/> is rejected inside a simulation).</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory factory, Func<TResult> function, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, creationOptions);
        }

        return StartNewCore(
            function,
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled full-scheduler <c>TaskFactory.StartNew&lt;TResult&gt;</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory factory,
        Func<TResult> function,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(function, cancellationToken, creationOptions, scheduler);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory.StartNew&lt;TResult&gt;</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory factory,
        Func<object?, TResult> function,
        object? state)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state);
        }

        return StartNewCore(
            () => function(state),
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory.StartNew&lt;TResult&gt;</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory factory,
        Func<object?, TResult> function,
        object? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, cancellationToken);
        }

        return StartNewCore(
            () => function(state),
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory.StartNew&lt;TResult&gt;</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory factory,
        Func<object?, TResult> function,
        object? state,
        TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, creationOptions);
        }

        return StartNewCore(
            () => function(state),
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled full-scheduler state-carrying <c>TaskFactory.StartNew&lt;TResult&gt;</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory factory,
        Func<object?, TResult> function,
        object? state,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(() => function(state), cancellationToken, creationOptions, scheduler, state);
    }

    /// <summary>Controlled <c>TaskFactory&lt;TResult&gt;.StartNew(Func&lt;TResult&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function);
        }

        return StartNewCore(
            function,
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory&lt;TResult&gt;.StartNew(Func&lt;TResult&gt;, CancellationToken)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken);
        }

        return StartNewCore(
            function,
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled <c>TaskFactory&lt;TResult&gt;.StartNew(Func&lt;TResult&gt;, TaskCreationOptions)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.AttachedToParent"/> is rejected inside a simulation).</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, creationOptions);
        }

        return StartNewCore(
            function,
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default);
    }

    /// <summary>Controlled full-scheduler <c>TaskFactory&lt;TResult&gt;.StartNew</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory<TResult> factory,
        Func<TResult> function,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(function, cancellationToken, creationOptions, scheduler);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory&lt;TResult&gt;.StartNew</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory<TResult> factory,
        Func<object?, TResult> function,
        object? state)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state);
        }

        return StartNewCore(
            () => function(state),
            factory.CancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory&lt;TResult&gt;.StartNew</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory<TResult> factory,
        Func<object?, TResult> function,
        object? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, cancellationToken);
        }

        return StartNewCore(
            () => function(state),
            cancellationToken,
            factory.CreationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled state-carrying <c>TaskFactory&lt;TResult&gt;.StartNew</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory<TResult> factory,
        Func<object?, TResult> function,
        object? state,
        TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, creationOptions);
        }

        return StartNewCore(
            () => function(state),
            factory.CancellationToken,
            creationOptions,
            factory.Scheduler ?? TaskScheduler.Default,
            state);
    }

    /// <summary>Controlled full-scheduler state-carrying <c>TaskFactory&lt;TResult&gt;.StartNew</c> overload.</summary>
    public static Task<TResult> StartNew<TResult>(
        TaskFactory<TResult> factory,
        Func<object?, TResult> function,
        object? state,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, state, cancellationToken, creationOptions, scheduler);
        }

        return StartNewCore(() => function(state), cancellationToken, creationOptions, scheduler, state);
    }

    private static Task<TResult> StartNewCore<TResult>(
        Func<TResult> function,
        CancellationToken cancellationToken,
        TaskCreationOptions creationOptions,
        TaskScheduler scheduler,
        object? asyncState = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(scheduler);
        RejectUnsupportedOptions(creationOptions);
        RejectUnsupportedScheduler(scheduler);
        var tcs = new TaskCompletionSource<TResult>(asyncState);
        ControlledTaskRuntime.QueueWork(
            () => RunFunc(function, tcs, cancellationToken),
            "System.Threading.Tasks.TaskFactory.StartNew");
        return tcs.Task;
    }

    private static void RunAction(Action action, TaskCompletionSource tcs, CancellationToken cancellationToken)
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
    }

    private static void RunFunc<TResult>(Func<TResult> function, TaskCompletionSource<TResult> tcs, CancellationToken cancellationToken)
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
    }

    private static void RejectUnsupportedOptions(TaskCreationOptions creationOptions)
    {
        const TaskCreationOptions supported =
            TaskCreationOptions.DenyChildAttach |
            TaskCreationOptions.HideScheduler;
        TaskCreationOptions unsupported = creationOptions & ~supported;
        if (unsupported != TaskCreationOptions.None)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.TaskFactory.StartNew",
                $"the creation option combination '{unsupported}' is not supported inside a simulation: the " +
                "cooperative scheduler cannot faithfully preserve parent attachment, fairness, long-running, " +
                "or asynchronous-continuation scheduling semantics.");
        }
    }

    private static void RejectUnsupportedScheduler(TaskScheduler scheduler)
    {
        if (!ReferenceEquals(scheduler, TaskScheduler.Default))
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.TaskFactory.StartNew",
                "a custom TaskScheduler is not supported inside a simulation because executing it would escape " +
                "Clockwork's controlled logical strand. Use TaskScheduler.Default or a factory without a custom scheduler.");
        }
    }

    #pragma warning restore CA1068
}
