using System.Globalization;

namespace Clockwork;

/// <summary>
/// A synchronization context that routes all continuations through a <see cref="SimulationTaskQueue"/>.
/// </summary>
public sealed class SimulationSynchronizationContext(SimulationTaskQueue taskQueue) : SynchronizationContext
{
    /// <inheritdoc />
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        taskQueue.Enqueue(new ScheduledSyncContextItem(d, state));
    }

    /// <summary>
    /// <para>
    /// Synchronously executes <paramref name="d"/> using the simulation's cooperative scheduling.
    /// There are two supported cases:
    /// </para>
    /// <para>
    /// 1. The calling thread is already running <em>on</em> this context's owning simulated
    /// operation - either because <see cref="SynchronizationContext.Current"/> is this context
    /// (or another instance backed by the same <see cref="SimulationTaskQueue"/>), or because
    /// <see cref="TaskScheduler.Current"/> is a <see cref="SimulationTaskScheduler"/> backed by the
    /// same queue. In that case <paramref name="d"/> runs immediately, inline, on the calling
    /// thread - it is already safe because nothing else can be running concurrently with it.
    /// </para>
    /// <para>
    /// 2. The calling thread is not on the owning operation, but no other thread currently holds
    /// the queue's single-threaded guard. The callback is scheduled onto the queue (preserving
    /// deterministic FIFO ordering relative to anything already pending) and this call then
    /// synchronously pumps the queue - via repeated <see cref="SimulationTaskQueue.RunOnce"/> calls
    /// on the calling thread - until the callback has executed, at which point <see cref="Send"/>
    /// returns. This can never deadlock: the calling thread does its own pumping rather than
    /// waiting for another thread to make progress, and every item already in the queue has a
    /// smaller sequence number than the scheduled callback, so pumping is guaranteed to reach it.
    /// </para>
    /// <para>
    /// If neither case applies - because another thread is genuinely, concurrently executing
    /// simulation work on this queue - this throws <see cref="InvalidOperationException"/> rather
    /// than attempting a real-time cross-thread wait (which nothing in the simulation would ever
    /// satisfy, since nothing pumps the queue autonomously). This is a real usage error: simulation
    /// code must not touch a queue from more than one thread at a time.
    /// </para>
    /// </summary>
    /// <inheritdoc />
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (IsOnOwningExecutionContext())
        {
            d(state);
            return;
        }

        ScheduleAndPumpUntilExecuted(d, state);
    }

    /// <summary>
    /// Determines whether the calling thread is already executing within the simulated operation
    /// that owns this context's underlying queue, per the two cases described on <see cref="Send"/>.
    /// </summary>
    private bool IsOnOwningExecutionContext()
    {
        var current = Current;
        if (current is not null && IsSameScheduler(current))
        {
            return true;
        }

        return TaskScheduler.Current is SimulationTaskScheduler simScheduler && simScheduler.IsSameScheduler(this);
    }

    /// <summary>
    /// Schedules <paramref name="d"/> onto the queue and pumps the queue on the calling thread
    /// until it has executed. Throws <see cref="InvalidOperationException"/> (via the queue's
    /// single-threaded guard) if another thread is genuinely, concurrently inside the queue.
    /// </summary>
    private void ScheduleAndPumpUntilExecuted(SendOrPostCallback d, object? state)
    {
        var executed = false;
        void WrappedCallback(object? s)
        {
            d(s);
            executed = true;
        }

        try
        {
            taskQueue.Enqueue(new ScheduledSyncContextItem(WrappedCallback, state));
        }
        catch (InvalidOperationException ex)
        {
#pragma warning disable EPC20 // Avoid using default ToString implementation
#pragma warning disable MA0150 // Do not call the default object.ToString explicitly
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Cannot synchronously execute callback {d} with state {state} via Send: another thread is concurrently using the simulation queue. {ex.Message}"),
                ex);
#pragma warning restore MA0150 // Do not call the default object.ToString explicitly
#pragma warning restore EPC20 // Avoid using default ToString implementation
        }

        while (!executed)
        {
            if (!taskQueue.RunOnce())
            {
                // Unreachable in practice: the scheduled callback is always due immediately (at
                // UtcNow) and its sequence number guarantees it is eventually the minimum ready
                // item, so the queue can never go idle before it runs.
                throw new InvalidOperationException("Send could not complete: the simulation queue went idle before the scheduled callback executed.");
            }
        }
    }

    /// <inheritdoc />
    public override SynchronizationContext CreateCopy() => new SimulationSynchronizationContext(taskQueue);

    /// <summary>
    /// Gets the underlying task queue for this synchronization context.
    /// </summary>
    public object UnderlyingScheduler => taskQueue;

    /// <summary>
    /// Checks if this synchronization context shares the same scheduler as another context.
    /// </summary>
    /// <param name="syncCtx">The context to compare with.</param>
    /// <returns>True if they share the same underlying scheduler.</returns>
    public bool IsSameScheduler(SynchronizationContext syncCtx) => ReferenceEquals(syncCtx, this) || syncCtx is SimulationSynchronizationContext simSyncCtx && simSyncCtx.UnderlyingScheduler.Equals(UnderlyingScheduler);

    /// <summary>
    /// Installs this synchronization context on the current thread and returns a scope
    /// that restores the previous context when disposed.
    /// If the current context is already this instance, or if the current TaskScheduler
    /// is a SimulationTaskScheduler sharing the same underlying queue, returns an empty
    /// scope (no-op) to avoid redundant context switching.
    /// </summary>
    /// <returns>A disposable scope that restores the previous synchronization context when disposed.</returns>
    public SynchronizationContextScope Install()
    {
        if (IsOnOwningExecutionContext())
        {
            return SynchronizationContextScope.Empty;
        }

        var previous = Current;
        SetSynchronizationContext(this);
        return new SynchronizationContextScope(previous);
    }

    private sealed class ScheduledSyncContextItem(SendOrPostCallback callback, object? state) : ScheduledItem
    {
        protected internal override void Invoke() => callback(state);
    }
}
