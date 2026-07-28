using System.Diagnostics;
using System.Globalization;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork;

/// <summary>
/// A node-scoped facade for scheduling callbacks on a <see cref="SimulationScheduler"/>.
/// The scheduler owns execution ordering, virtual time, and timers; a lane contributes only
/// node identity, synchronization-context installation, and pending-item diagnostics.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
[DebuggerTypeProxy(typeof(SimulationSchedulerLaneDebugView))]
public sealed class SimulationSchedulerLane
{
    private readonly SortedSet<ScheduledItem> _items = new(ScheduledItem.Comparer);
    private readonly SimulationScheduler _scheduler;
    private readonly SingleThreadedGuard _guard;
    private readonly SimulationNodeIdentity? _node;
    private long _sequenceNumber;

    /// <summary>Initializes a lane owned by <paramref name="scheduler"/>.</summary>
    public SimulationSchedulerLane(
        SimulationScheduler scheduler,
        SingleThreadedGuard guard,
        SimulationNodeIdentity? node = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(guard);
        _scheduler = scheduler;
        _guard = guard;
        _node = node;
        SynchronizationContext = new SimulationSynchronizationContext(this);
        ScheduledItems = _items.AsReadOnly();
    }

    /// <summary>Gets pending items ordered by due time and registration sequence.</summary>
    public IReadOnlySet<ScheduledItem> ScheduledItems { get; }

    /// <summary>Gets the scheduler-backed current simulated date/time.</summary>
    public DateTimeOffset UtcNow => _scheduler.UtcNow;

    /// <summary>Gets the synchronization context which posts callbacks to this lane.</summary>
    public SimulationSynchronizationContext SynchronizationContext { get; }

    internal SimulationScheduler Scheduler => _scheduler;

    /// <summary>Gets whether this lane has pending items.</summary>
    public bool HasItems
    {
        get
        {
            using var _ = _guard.Enter();
            return _items.Count > 0;
        }
    }

    /// <summary>Gets the earliest future due time, if any.</summary>
    public DateTimeOffset? NextWaitingDueTime
    {
        get
        {
            using var _ = _guard.Enter();
            foreach (var item in _items)
            {
                if (item.DueTime > UtcNow)
                {
                    return item.DueTime;
                }
            }

            return null;
        }
    }

    /// <summary>Schedules an item immediately.</summary>
    public void Enqueue(ScheduledItem item) => Schedule(item, TimeSpan.Zero);

    /// <summary>Schedules an action after a modeled delay.</summary>
    public void EnqueueAfter(Action action, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = EnqueueAfter(new ScheduledActionItem(action), delay);
    }

    /// <summary>Schedules an item after a modeled delay.</summary>
    public TItem EnqueueAfter<TItem>(TItem item, TimeSpan delay)
        where TItem : ScheduledItem
    {
        Schedule(item, delay);
        return item;
    }

    private void Schedule(ScheduledItem item, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        using var _ = _guard.Enter();
        var dueTime = UtcNow + delay;
        item.OnScheduled(this, dueTime, _sequenceNumber++);
        _items.Add(item);

        IDisposable registration;
        try
        {
            registration = _scheduler.ScheduleAfter(
                "Simulation lane callback",
                () => Execute(item),
                delay,
                _node);
        }
        catch
        {
            _items.Remove(item);
            item.OnRemoved();
            throw;
        }

        item.SetCancellation(registration);
    }

    internal void RemoveItem(ScheduledItem item)
    {
        using var _ = _guard.Enter();
        if (_items.Remove(item))
        {
            item.CancelRegistration();
        }
    }

    private void Execute(ScheduledItem item)
    {
        using var _ = _guard.Enter();
        if (!_items.Remove(item))
        {
            return;
        }

        item.OnInvoking();
        using var syncScope = SynchronizationContext.Install();
        try
        {
            item.Invoke();
        }
        catch (Exception exception)
        {
            _scheduler.ReportUnhandledCallbackException(exception);
            throw;
        }
    }

    /// <summary>Runs one scheduler operation without advancing virtual time.</summary>
    public bool RunOnce()
    {
        using var control = _scheduler.EnterControlScope();
        return _scheduler.RunStep();
    }

    /// <summary>Runs scheduler operations until none is currently eligible.</summary>
    public int RunUntilIdle()
    {
        using var control = _scheduler.EnterControlScope();
        var count = 0;
        while (_scheduler.RunStep())
        {
            count++;
        }

        return count;
    }

    internal void SetEnabled(bool enabled)
    {
        if (_node is { } node)
        {
            _scheduler.SetNodeEnabled(node, enabled);
        }
    }

    private string DebuggerDisplay =>
        string.Create(CultureInfo.InvariantCulture, $"Count={_items.Count} UtcNow={UtcNow:HH:mm:ss.fff}");
}

internal sealed class SimulationSchedulerLaneDebugView(SimulationSchedulerLane lane)
{
    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public ScheduledItem[] Items => [.. lane.ScheduledItems];
}
