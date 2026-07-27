using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork;

/// <summary>
/// <para>
/// A time-aware task queue that serves as the common core for both
/// <see cref="TaskScheduler"/> and <see cref="SimulationTimeProvider"/>.
/// </para>
/// <para>
/// Items are stored in a single queue ordered by due time, then sequence number.
/// Items with DueTime &lt;= UtcNow are considered "ready" for execution.
/// This enables deterministic simulation testing by providing unified control
/// over task execution order and time advancement.
/// </para>
/// <para>
/// The queue delegates time to a shared <see cref="SimulationClock"/>,
/// enabling multiple queues to share a unified view of time.
/// </para>
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[DebuggerTypeProxy(typeof(SimulationTaskQueueDebugView))]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "TaskQueue accurately describes this component's purpose")]
public sealed class SimulationTaskQueue
{
    // Single queue ordered by due time, then sequence number
    private readonly SortedSet<ScheduledItem> _queue = new(ScheduledItem.Comparer);
    private readonly SimulationClock _clock;
    private readonly SingleThreadedGuard _guard;
    private readonly SimulationAmbientContextConfiguration? _ambientContext;
    private readonly ControlledOperationScheduler? _operationScheduler;
    private long _sequenceNumber;

    /// <summary>
    /// Gets the scheduled items in the queue, ordered by due time then sequence number.
    /// This is a read-only view that cannot be modified.
    /// </summary>
    public IReadOnlySet<ScheduledItem> ScheduledItems { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationTaskQueue"/> class.
    /// Creates a new simulation task queue that uses the specified clock for time.
    /// Multiple queues can share the same clock for unified time coordination.
    /// </summary>
    /// <param name="clock">The clock to use for time.</param>
    /// <param name="guard">The single-threaded guard used to detect concurrent access on simulation-thread-only operations.</param>
    /// <param name="ambientContext">
    /// Optional ambient-context configuration (see <see cref="SimulationAmbientContextConfiguration"/>).
    /// When provided, <see cref="RunOnce"/> validates external entry and installs/restores the
    /// configured runtime (and, if present, node) as ambient <see cref="SimulationExecutionContext"/>
    /// around each executed item. When omitted (the default), this queue behaves exactly as it did
    /// before ambient-context integration existed.
    /// </param>
    /// <param name="operationScheduler">
    /// <para>
    /// Optional controlled-operation kernel (see
    /// <see cref="Clockwork.Runtime.Scheduling.ControlledOperationScheduler"/>). This is the opt-in
    /// Phase 3A compatibility bridge: when both this and <paramref name="ambientContext"/> are
    /// supplied, <see cref="RunOnce"/> runs each ready item as a single controlled operation instead
    /// of invoking it inline, so the item body executes under the kernel's permission baton and
    /// carries a logical execution identity. It is <see langword="null"/> by default, so the queue's
    /// established behavior - and every existing trace snapshot - is completely unchanged unless a
    /// host explicitly opts in.
    /// </para>
    /// <para>
    /// Granularity is deliberately one controlled operation per ready item (per user-visible
    /// callback), not one per internal bookkeeping call (enqueue/remove/peek), to give a meaningful
    /// operation boundary without churning behavior. The kernel's single-baton guarantee makes the
    /// controlled path observably equivalent to the inline path for callbacks that enqueue further
    /// work and return; re-entrantly driving the queue from within a running item (a synchronous
    /// pump) is a resource-wait scenario deferred to Phase 3B.
    /// </para>
    /// </param>
    public SimulationTaskQueue(SimulationClock clock, SingleThreadedGuard guard, SimulationAmbientContextConfiguration? ambientContext = null, ControlledOperationScheduler? operationScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(guard);
        _clock = clock;
        _guard = guard;
        _ambientContext = ambientContext;
        _operationScheduler = operationScheduler;
        SynchronizationContext = new SimulationSynchronizationContext(this);
        ScheduledItems = _queue.AsReadOnly();
    }

    /// <summary>
    /// Gets the current simulated date/time.
    /// </summary>
    public DateTimeOffset UtcNow => _clock.UtcNow;

    /// <summary>
    /// Gets the synchronization context used to execute callbacks.
    /// </summary>
    public SimulationSynchronizationContext SynchronizationContext { get; }

    /// <summary>
    /// Gets a value indicating whether gets whether there are any items in the queue.
    /// This is called from the simulation thread only.
    /// </summary>
    public bool HasItems
    {
        get
        {
            using var _ = _guard.Enter();
            return _queue.Count > 0;
        }
    }

    /// <summary>
    /// Gets the due time of the next waiting (not yet ready) task, or null if no waiting tasks exist.
    /// This is called from the simulation thread only.
    /// </summary>
    public DateTimeOffset? NextWaitingDueTime
    {
        get
        {
            using var _ = _guard.Enter();
            foreach (var item in _queue)
            {
                if (item.DueTime > UtcNow)
                    return item.DueTime;
            }

            return null;
        }
    }

    /// <summary>
    /// Enqueues a scheduled item to be executed immediately (at current time).
    /// The item's DueTime, SequenceNumber, and queue reference are set by this method.
    /// This method must be called from the simulation thread - the guard will throw
    /// if called from another thread, indicating async work has escaped the simulation.
    /// </summary>
    /// <param name="item">The scheduled item to enqueue.</param>
    public void Enqueue(ScheduledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var _ = _guard.Enter();
        ScheduleCore(item, UtcNow);
    }

    /// <summary>
    /// Enqueues an action to be executed after a delay from the current time.
    /// Convenience method that creates a <see cref="ScheduledActionItem"/>.
    /// This is called from the simulation thread only.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="delay">The delay from the current time.</param>
    public void EnqueueAfter(Action action, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        using var _ = _guard.Enter();
        ScheduleCore(new ScheduledActionItem(action), UtcNow + delay);
    }

    /// <summary>
    /// Enqueues an item to be executed after a delay from the current time.
    /// This is called from the simulation thread only.
    /// </summary>
    /// <param name="item">The item to execute.</param>
    /// <param name="delay">The delay from the current time.</param>
    public TItem EnqueueAfter<TItem>(TItem item, TimeSpan delay)
        where TItem : ScheduledItem
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        using var _ = _guard.Enter();
        ScheduleCore(item, UtcNow + delay);
        return item;
    }

    /// <summary>
    /// Schedules an item to be executed at a specific absolute time.
    /// The item's DueTime, SequenceNumber, and queue reference are set by this method.
    /// Returns the scheduled item which can be disposed to cancel it.
    /// CALLER MUST HOLD _guard.
    /// </summary>
    /// <param name="item">The scheduled item to schedule.</param>
    /// <param name="dueTime">The absolute time when the item should be executed.</param>
    private void ScheduleCore(ScheduledItem item, DateTimeOffset dueTime)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.OnScheduled(this, dueTime, _sequenceNumber++);
        _queue.Add(item);
    }

    /// <summary>
    /// Removes an item from the queue. Called by ScheduledItem.Dispose().
    /// This method must be called from the simulation thread.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    internal void RemoveItem(ScheduledItem item)
    {
        using var _ = _guard.Enter();
        _queue.Remove(item);
    }

    /// <summary>
    /// Tries to dequeue and execute the next ready item.
    /// This is called from the simulation thread only.
    /// </summary>
    /// <returns>True if an item was dequeued and executed, false if no items are ready.</returns>
    public bool RunOnce()
    {
        if (_operationScheduler is not null && _ambientContext is { } controlledAmbient)
        {
            return RunOnceControlled(controlledAmbient);
        }

        using var _ = _guard.Enter();
        if (_queue.Count == 0)
            return false;

        var item = _queue.Min!;
        if (item.DueTime > UtcNow)
            return false; // No ready items

        _queue.Remove(item);
        ExecuteReadyItem(item);

        return true;
    }

    /// <summary>
    /// The opt-in Phase 3A compatibility path for <see cref="RunOnce"/>: dequeues the next ready item
    /// exactly as the inline path does, then runs it as a single controlled operation on the kernel's
    /// permission baton instead of invoking it inline. The single-threaded guard is held across the
    /// baton handoff under the scheduler's control scope, so the controlling thread and the operation
    /// thread count as one logical owner (a legitimate handoff is reentrant) while genuinely escaped
    /// async work is still detected. Ambient runtime/node identity is established by the kernel on the
    /// operation thread; the operation additionally carries a logical execution identity, which the
    /// inline path does not. Exceptions thrown by the item are re-thrown with their original identity
    /// so callers observe the same failure semantics as the inline path.
    /// </summary>
    /// <param name="ambient">The ambient-context configuration to run the item under.</param>
    /// <returns><see langword="true"/> if an item was dequeued and run; otherwise <see langword="false"/>.</returns>
    private bool RunOnceControlled(SimulationAmbientContextConfiguration ambient)
    {
        using var control = _operationScheduler!.EnterControlScope();
        using var _ = _guard.Enter();
        if (_queue.Count == 0)
            return false;

        var item = _queue.Min!;
        if (item.DueTime > UtcNow)
            return false; // No ready items

        _queue.Remove(item);

        SimulationExternalEntryGuard.ValidateEntry(ambient.Runtime, "SimulationTaskQueue.RunOnce");
        Exception? captured = null;
        _operationScheduler.Schedule(
            "SimulationTaskQueue.Item",
            () =>
            {
                using var syncScope = SynchronizationContext.Install();
                try
                {
                    item.Invoke();
                }
                catch (Exception ex)
                {
                    // Preserve the exact exception so RunOnce can re-throw it with original identity,
                    // matching the inline path, while still letting the kernel record terminal state.
                    captured = ex;
                    throw;
                }
            },
            ambient.Node);

        _operationScheduler.RunStep();

        if (captured is not null)
        {
            ExceptionDispatchInfo.Throw(captured);
        }

        return true;
    }

    /// <summary>
    /// Executes one already-dequeued ready item, installing this queue's synchronization context
    /// and - when this queue has an <see cref="SimulationAmbientContextConfiguration"/> - validating
    /// external entry and installing/restoring the configured ambient
    /// <see cref="SimulationExecutionContext"/> scope around the invocation. Queues without an
    /// ambient context configured skip that entirely, preserving prior behavior exactly.
    /// </summary>
    /// <param name="item">The already-dequeued item to invoke.</param>
    private void ExecuteReadyItem(ScheduledItem item)
    {
        if (_ambientContext is { } ambient)
        {
            SimulationExternalEntryGuard.ValidateEntry(ambient.Runtime, "SimulationTaskQueue.RunOnce");
            using var runtimeScope = SimulationExecutionContext.EnterRuntime(ambient.ActivationToken, ambient.Runtime);
            using var nodeScope = ambient.Node is { } node ? SimulationExecutionContext.EnterNode(node) : null;
            using var syncScope = SynchronizationContext.Install();
            item.Invoke();
        }
        else
        {
            using var syncScope = SynchronizationContext.Install();
            item.Invoke();
        }
    }

    /// <summary>
    /// Executes all ready items in the queue.
    /// </summary>
    /// <returns>The number of items executed.</returns>
    public int RunUntilIdle()
    {
        var count = 0;
        while (RunOnce())
        {
            count++;
        }

        return count;
    }

    private string DebuggerDisplay => string.Create(CultureInfo.InvariantCulture, $"Count={_queue.Count} UtcNow={UtcNow:HH:mm:ss.fff}");
}

/// <summary>
/// Debug view for SimulationTaskQueue that shows scheduled items in a more readable format.
/// </summary>
internal sealed class SimulationTaskQueueDebugView(SimulationTaskQueue queue)
{
    private readonly SimulationTaskQueue _queue = queue;

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public ScheduledItem[] Items => [.. _queue.ScheduledItems];
}
