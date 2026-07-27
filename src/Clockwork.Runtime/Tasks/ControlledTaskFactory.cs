using System.Threading.Tasks;

namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// Static shims for the <see cref="TaskFactory"/> and <see cref="TaskFactory{TResult}"/> surface. The
/// rewriter redirects the supported call sites here; instance members are exposed as static methods
/// whose first parameter is the receiver, matching Clockwork's <c>RedirectCall</c> convention.
/// </para>
/// <para>
/// <see cref="TaskFactory.StartNew(System.Action)"/> and its generic counterpart schedule work onto a
/// <see cref="TaskScheduler"/> - by default the thread pool - which is exactly the uncontrolled
/// offloading that Phase 6A refuses (thread-pool scheduling is owned by the threading phase, mirroring
/// the <c>Task.Run</c> decision). These shims therefore <b>reject</b> the call under an active simulation
/// with a precise diagnostic rather than silently letting work escape onto a physical thread, and run
/// the real BCL API unchanged outside a simulation.
/// </para>
/// </summary>
public static class ControlledTaskFactory
{
    /// <summary>
    /// Rejects <c>TaskFactory.StartNew(Action)</c> under simulation: it offloads work onto a task
    /// scheduler (the thread pool by default), which Phase 6A does not control.
    /// </summary>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="action">The work to schedule.</param>
    /// <returns>The scheduled task, only when no simulation is active.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Thrown inside a simulation.</exception>
    public static Task StartNew(TaskFactory factory, Action action)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RejectIfSimulated();
        return factory.StartNew(action);
    }

    /// <summary>
    /// Rejects <c>TaskFactory.StartNew&lt;TResult&gt;(Func&lt;TResult&gt;)</c> under simulation - the common
    /// <c>Task.Factory.StartNew(() =&gt; ...)</c> form on the non-generic factory - for the same reason.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <returns>The scheduled task, only when no simulation is active.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Thrown inside a simulation.</exception>
    public static Task<TResult> StartNew<TResult>(TaskFactory factory, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RejectIfSimulated();
        return factory.StartNew(function);
    }

    /// <summary>
    /// Rejects <c>TaskFactory&lt;TResult&gt;.StartNew(Func&lt;TResult&gt;)</c> under simulation: it offloads
    /// work onto a task scheduler (the thread pool by default), which Phase 6A does not control.
    /// </summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="factory">The receiving factory.</param>
    /// <param name="function">The work to schedule.</param>
    /// <returns>The scheduled task, only when no simulation is active.</returns>
    /// <exception cref="ControlledTaskUnsupportedException">Thrown inside a simulation.</exception>
    public static Task<TResult> StartNew<TResult>(TaskFactory<TResult> factory, Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RejectIfSimulated();
        return factory.StartNew(function);
    }

    private static void RejectIfSimulated()
    {
        if (ControlledTaskRuntime.IsSimulationActive)
        {
            throw new ControlledTaskUnsupportedException(
                "System.Threading.Tasks.TaskFactory.StartNew",
                "TaskFactory.StartNew offloads work onto a task scheduler (the thread pool by default); " +
                "thread-pool scheduling is owned by the threading phase, so Phase 6A refuses to let the work " +
                "escape onto an uncontrolled physical thread. Await the work directly instead.");
        }
    }
}
