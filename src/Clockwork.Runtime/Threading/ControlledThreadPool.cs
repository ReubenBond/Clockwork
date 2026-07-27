using System.Threading;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="ThreadPool"/> queueing surface. The rewriter redirects the supported
/// call sites here: <see cref="ThreadPool.QueueUserWorkItem(WaitCallback)"/> and its overloads queue the
/// callback as a fresh controlled operation on the simulation coordinator instead of dispatching it to a
/// physical thread-pool thread, so it runs deterministically on the single logical thread. Outside a
/// simulation every shim delegates to the real BCL API unchanged.
/// </para>
/// <para>
/// <b>ExecutionContext flow.</b> The BCL draws a deliberate distinction between the <em>safe</em>
/// <c>QueueUserWorkItem</c> family, which captures the caller's <see cref="ExecutionContext"/> and runs
/// the callback under it (so ambient <see cref="AsyncLocal{T}"/> values flow to the callback), and the
/// <em>unsafe</em> <c>UnsafeQueueUserWorkItem</c> family, which does not capture the caller's context.
/// The shims preserve that distinction: the safe variants snapshot the context at enqueue time with
/// <see cref="ExecutionContext.Capture"/> and run the callback via <see cref="ExecutionContext.Run"/>,
/// while the unsafe variants queue the callback directly, so it observes only the ambient context at run
/// time rather than the caller's captured snapshot.
/// </para>
/// <para>
/// This goes beyond Microsoft Coyote, which routes thread-pool work through its controlled
/// <c>Task</c>/<c>TaskFactory</c> types and does not intercept <c>ThreadPool.QueueUserWorkItem</c>
/// directly. The native-I/O surface (<c>UnsafeQueueNativeOverlapped</c>) and, until Phase 7, the
/// registered-wait surface cannot be modelled faithfully and are rejected precisely - see
/// <see cref="ControlledThreadPoolUnsupportedException"/>.
/// </para>
/// </summary>
public static class ControlledThreadPool
{
    private const string QueueApi = "System.Threading.ThreadPool.QueueUserWorkItem";
    private const string UnsafeQueueApi = "System.Threading.ThreadPool.UnsafeQueueUserWorkItem";

    /// <summary>Controlled <see cref="ThreadPool.QueueUserWorkItem(WaitCallback)"/> (flows ExecutionContext).</summary>
    /// <param name="callBack">The work item to queue.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool QueueUserWorkItem(WaitCallback callBack)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        return QueueUserWorkItem(callBack, state: null);
    }

    /// <summary>Controlled <see cref="ThreadPool.QueueUserWorkItem(WaitCallback, object)"/> (flows ExecutionContext).</summary>
    /// <param name="callBack">The work item to queue.</param>
    /// <param name="state">The object passed to the callback.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool QueueUserWorkItem(WaitCallback callBack, object? state)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return ThreadPool.QueueUserWorkItem(callBack, state);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        ControlledTaskRuntime.QueueWork(() => RunFlowed(context, () => callBack(state)), QueueApi);
        return true;
    }

    /// <summary>Controlled generic <see cref="ThreadPool.QueueUserWorkItem{TState}(Action{TState}, TState, bool)"/> (flows ExecutionContext).</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="callBack">The work item to queue.</param>
    /// <param name="state">The strongly-typed state passed to the callback.</param>
    /// <param name="preferLocal">Honoured only outside a simulation; the controlled scheduler has a single queue.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool QueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return ThreadPool.QueueUserWorkItem(callBack, state, preferLocal);
        }

        ExecutionContext? context = ExecutionContext.Capture();
        ControlledTaskRuntime.QueueWork(() => RunFlowed(context, () => callBack(state)), QueueApi);
        return true;
    }

    /// <summary>Controlled <see cref="ThreadPool.UnsafeQueueUserWorkItem(WaitCallback, object)"/> (does not flow ExecutionContext).</summary>
    /// <param name="callBack">The work item to queue.</param>
    /// <param name="state">The object passed to the callback.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object? state)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return ThreadPool.UnsafeQueueUserWorkItem(callBack, state);
        }

        // Unsafe variants do not capture the caller's ExecutionContext, so the callback observes only the
        // ambient run-time context, not the caller's enqueue-time snapshot.
        ControlledTaskRuntime.QueueWork(() => callBack(state), UnsafeQueueApi);
        return true;
    }

    /// <summary>Controlled <see cref="ThreadPool.UnsafeQueueUserWorkItem(IThreadPoolWorkItem, bool)"/> (does not flow ExecutionContext).</summary>
    /// <param name="callBack">The work item to queue.</param>
    /// <param name="preferLocal">Honoured only outside a simulation; the controlled scheduler has a single queue.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool UnsafeQueueUserWorkItem(IThreadPoolWorkItem callBack, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return ThreadPool.UnsafeQueueUserWorkItem(callBack, preferLocal);
        }

        ControlledTaskRuntime.QueueWork(callBack.Execute, UnsafeQueueApi);
        return true;
    }

    /// <summary>Controlled generic <see cref="ThreadPool.UnsafeQueueUserWorkItem{TState}(Action{TState}, TState, bool)"/> (does not flow ExecutionContext).</summary>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="callBack">The work item to queue.</param>
    /// <param name="state">The strongly-typed state passed to the callback.</param>
    /// <param name="preferLocal">Honoured only outside a simulation; the controlled scheduler has a single queue.</param>
    /// <returns><see langword="true"/> - the work item is always accepted.</returns>
    public static bool UnsafeQueueUserWorkItem<TState>(Action<TState> callBack, TState state, bool preferLocal)
    {
        ArgumentNullException.ThrowIfNull(callBack);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return ThreadPool.UnsafeQueueUserWorkItem(callBack, state, preferLocal);
        }

        ControlledTaskRuntime.QueueWork(() => callBack(state), UnsafeQueueApi);
        return true;
    }

    /// <summary>
    /// Rejection injected before an unsupported native-I/O thread-pool call site
    /// (<c>UnsafeQueueNativeOverlapped</c>), which cannot be modelled by the deterministic scheduler.
    /// </summary>
    /// <param name="apiName">The unsupported API, supplied by the rewriter.</param>
    public static void RejectNativeOverlapped(string apiName) =>
        throw new ControlledThreadPoolUnsupportedException(
            apiName,
            "native overlapped I/O cannot be modelled by the deterministic scheduler; the controlled " +
            "thread pool has no OS I/O completion port.");

    private static void RunFlowed(ExecutionContext? context, Action work)
    {
        if (context is null)
        {
            work();
            return;
        }

        ExecutionContext.Run(context, static state => ((Action)state!)(), work);
    }
}
