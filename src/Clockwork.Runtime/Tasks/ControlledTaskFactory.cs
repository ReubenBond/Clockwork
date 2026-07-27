using System.Threading;
using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks;

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
/// <see cref="TaskCreationOptions.AttachedToParent"/> option is rejected under simulation: the
/// cooperative model has no faithful parent/child attach relationship, so silently ignoring it would
/// change observable completion semantics. Every option combination runs unchanged outside a simulation.
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
        return StartNew(factory, action, factory.CreationOptions, factory.CancellationToken);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew(Action, CancellationToken)</c>.</summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <param name="cancellationToken">A token that cancels the work before it starts.</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task StartNew(TaskFactory factory, Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return StartNew(factory, action, factory.CreationOptions, cancellationToken);
    }

    /// <summary>Controlled <c>TaskFactory.StartNew(Action, TaskCreationOptions)</c>.</summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <param name="creationOptions">The creation options (<see cref="TaskCreationOptions.AttachedToParent"/> is rejected inside a simulation).</param>
    /// <returns>A task that completes when the work does.</returns>
    public static Task StartNew(TaskFactory factory, Action action, TaskCreationOptions creationOptions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return StartNew(factory, action, creationOptions, factory.CancellationToken);
    }

    private static Task StartNew(TaskFactory factory, Action action, TaskCreationOptions creationOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(action, cancellationToken, creationOptions, factory.Scheduler ?? TaskScheduler.Current);
        }

        RejectUnsupportedOptions(creationOptions);
        var tcs = new TaskCompletionSource();
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
        return StartNew(factory, function, factory.CreationOptions, factory.CancellationToken);
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
        return StartNew(factory, function, factory.CreationOptions, cancellationToken);
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
        return StartNew(factory, function, creationOptions, factory.CancellationToken);
    }

    private static Task<TResult> StartNew<TResult>(TaskFactory factory, Func<TResult> function, TaskCreationOptions creationOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken, creationOptions, factory.Scheduler ?? TaskScheduler.Current);
        }

        RejectUnsupportedOptions(creationOptions);
        var tcs = new TaskCompletionSource<TResult>();
        ControlledTaskRuntime.QueueWork(
            () => RunFunc(function, tcs, cancellationToken),
            "System.Threading.Tasks.TaskFactory.StartNew");
        return tcs.Task;
    }

    /// <summary>Controlled <c>TaskFactory&lt;TResult&gt;.StartNew(Func&lt;TResult&gt;)</c>.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <returns>A task with the function's result.</returns>
    public static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return StartNew(factory, function, factory.CreationOptions, factory.CancellationToken);
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
        return StartNew(factory, function, factory.CreationOptions, cancellationToken);
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
        return StartNew(factory, function, creationOptions, factory.CancellationToken);
    }

    private static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function, TaskCreationOptions creationOptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return factory.StartNew(function, cancellationToken, creationOptions, factory.Scheduler ?? TaskScheduler.Current);
        }

        RejectUnsupportedOptions(creationOptions);
        var tcs = new TaskCompletionSource<TResult>();
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
        if ((creationOptions & TaskCreationOptions.AttachedToParent) != 0)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.TaskFactory.StartNew",
                "the AttachedToParent creation option is not supported inside a simulation: the cooperative " +
                "controlled scheduler has no faithful parent/child attach relationship, so honouring it would " +
                "change observable completion semantics. Remove AttachedToParent (or use DenyChildAttach).");
        }
    }
}
