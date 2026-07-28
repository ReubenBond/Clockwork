namespace Clockwork;

/// <summary>
/// A timer implementation for the simulation time provider.
/// This implements the timer abstractions using a scheduler lane.
/// </summary>
public sealed class SimulationTimer(SimulationSchedulerLane schedulerLane, TimerCallback callback, object? state) : ITimer
{
    private const uint MaxSupportedTimeout = 0xfffffffe;

    private readonly TimerCallback? _callback = callback;
    private readonly object? _state = state;
    private SimulationSchedulerLane? _schedulerLane = schedulerLane;
    private ScheduledTimerItem? _scheduledTimer;
    private long _generation;

    /// <summary>
    /// Gets the current period for this timer.
    /// </summary>
    public TimeSpan Period { get; private set; }

    /// <inheritdoc />
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        var dueTimeMs = (long)dueTime.TotalMilliseconds;
        var periodMs = (long)period.TotalMilliseconds;

        // -1 means infinite (valid), otherwise must be non-negative and within MaxSupportedTimeout
        if (dueTimeMs < -1)
            throw new ArgumentOutOfRangeException(nameof(dueTime));
        if (dueTimeMs != -1 && (ulong)dueTimeMs > MaxSupportedTimeout)
            throw new ArgumentOutOfRangeException(nameof(dueTime));
        if (periodMs < -1)
            throw new ArgumentOutOfRangeException(nameof(period));
        if (periodMs != -1 && (ulong)periodMs > MaxSupportedTimeout)
            throw new ArgumentOutOfRangeException(nameof(period));

        var queue = _schedulerLane;
        if (queue is null)
        {
            // timer has been disposed
            return false;
        }

        _generation++;

        // Cancel any existing timer
        _scheduledTimer?.Dispose();
        _scheduledTimer = null;

        if (dueTimeMs < 0)
        {
            // Infinite due time means the timer is disabled
            Period = TimeSpan.Zero;
            return true;
        }

        if (periodMs < 0 || periodMs == Timeout.Infinite)
        {
            // Normalize period
            period = TimeSpan.Zero;
        }

        Period = period;

        // Schedule the new timer
        ScheduleNextFiring(queue, dueTime);

        return true;
    }

    private void ScheduleNextFiring(SimulationSchedulerLane queue, TimeSpan delay)
    {
        _scheduledTimer = queue.EnqueueAfter(
            new ScheduledTimerItem(this, _generation),
            delay);
    }

    private void TimerFired(long generation)
    {
        if (_generation == generation)
        {
            _scheduledTimer = null;
        }

        // Invoke the user callback
        _callback!(_state);

        // A callback can reentrantly change or dispose the timer. Only the generation which
        // actually fired may perform its automatic periodic reschedule.
        var queue = _schedulerLane;
        if (_generation == generation && queue is not null && Period > TimeSpan.Zero)
        {
            ScheduleNextFiring(queue, Period);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _generation++;
        _scheduledTimer?.Dispose();
        _scheduledTimer = null;
        _schedulerLane = null;
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Gets information about all pending timers from the scheduler lane.
    /// </summary>
    /// <param name="schedulerLane">The scheduler lane to query.</param>
    /// <returns>A list of timer info for all pending timers.</returns>
    public static IReadOnlyList<(DateTimeOffset DueTime, TimeSpan Period)> GetTimers(SimulationSchedulerLane schedulerLane)
    {
        ArgumentNullException.ThrowIfNull(schedulerLane);
        return schedulerLane.GetItemsOfType<ScheduledTimerItem, (DateTimeOffset, TimeSpan)>(timer => (timer.DueTime, timer.Timer.Period));
    }

    /// <summary>
    /// Gets the count of pending timers from the scheduler lane.
    /// </summary>
    /// <param name="schedulerLane">The scheduler lane to query.</param>
    /// <returns>The count of pending timers.</returns>
    public static int GetPendingTimerCount(SimulationSchedulerLane schedulerLane)
    {
        ArgumentNullException.ThrowIfNull(schedulerLane);
        return schedulerLane.GetWaitingCount<ScheduledTimerItem>();
    }

    private sealed class ScheduledTimerItem(SimulationTimer timer, long generation) : ScheduledItem
    {
        public SimulationTimer Timer => timer;

        protected internal override void Invoke() => timer.TimerFired(generation);
    }
}
