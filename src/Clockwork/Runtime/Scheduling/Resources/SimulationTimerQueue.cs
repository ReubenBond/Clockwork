using System.Globalization;

namespace Clockwork.Runtime.Scheduling.Resources;

/// <summary>
/// <para>
/// The scheduler's source of modeled (virtual) time and ordered set of pending timer registrations:
/// a monotonic <see cref="Now"/> offset from simulation start plus a due-time-ordered queue.
/// </para>
/// <para>
/// <c>SimulationClock</c> is a read-only host facade over this scheduler-owned timeline; there is no
/// second clock to keep synchronized.
/// </para>
/// <para>
/// <b>Threading.</b> This type performs no locking of its own. The owning
/// <see cref="SimulationScheduler"/> only ever touches it while holding its scheduler gate,
/// so every read and mutation is already serialized. Timeouts fire <i>only</i> when the scheduler
/// explicitly advances time (which it does only when no operation is runnable), never from a
/// real-time timer, which is what makes a timeout a deterministic, replayable event.
/// </para>
/// </summary>
internal sealed class SimulationTimerQueue
{
    private readonly List<ISimulationTimerEntry> _pending = [];
    private TimeSpan _now;
    private long _nextSequence;

    /// <summary>Gets the current modeled time as a monotonic, non-negative offset from simulation start.</summary>
    public TimeSpan Now => _now;

    /// <summary>Gets the number of live (non-canceled) pending timeout registrations.</summary>
    public int PendingCount
    {
        get
        {
            var count = 0;
            foreach (var registration in _pending)
            {
                if (!registration.IsCanceled)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets a value indicating whether any live timeout is pending.</summary>
    public bool HasPending
    {
        get
        {
            foreach (var registration in _pending)
            {
                if (!registration.IsCanceled)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Registers a finite timeout for <paramref name="waiter"/> that becomes due at
    /// <see cref="Now"/> + <paramref name="delay"/>. The caller is responsible for zero and infinite
    /// timeouts (which never produce a registration).
    /// </summary>
    /// <param name="delay">A strictly positive delay from the current modeled time.</param>
    /// <param name="waiter">The waiter this timeout will resolve when it fires.</param>
    /// <returns>The created registration, already linked to the waiter's <see cref="SimulationResourceWaiter.Timeout"/> by the caller.</returns>
    public SimulationResourceTimeoutRegistration Schedule(TimeSpan delay, SimulationResourceWaiter waiter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        var registration = new SimulationResourceTimeoutRegistration(
            SimulationDeadlineMath.SaturatingAdd(_now, delay),
            ++_nextSequence,
            waiter);
        _pending.Add(registration);
        return registration;
    }

    public SimulationTimerRegistration Schedule(
        TimeSpan delay,
        Action? onElapsed,
        string? diagnosticKind = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        var registration = new SimulationTimerRegistration(
            SimulationDeadlineMath.SaturatingAdd(_now, delay),
            ++_nextSequence,
            onElapsed,
            diagnosticKind);
        _pending.Add(registration);
        return registration;
    }

    /// <summary>
    /// Gets the earliest due instant among live registrations, or <see langword="null"/> when none is
    /// pending. Canceled registrations are ignored.
    /// </summary>
    public TimeSpan? NextDueTime()
    {
        TimeSpan? earliest = null;
        foreach (var registration in _pending)
        {
            if (registration.IsCanceled)
            {
                continue;
            }

            if (earliest is null || registration.DueTime < earliest.Value)
            {
                earliest = registration.DueTime;
            }
        }

        return earliest;
    }

    /// <summary>
    /// Advances <see cref="Now"/> to the earliest pending due instant and returns every live
    /// registration due at exactly that instant, ordered by their deterministic registration
    /// sequence. Canceled registrations are purged. Returns an empty list (leaving time unchanged)
    /// when nothing is pending.
    /// </summary>
    public IReadOnlyList<ISimulationTimerEntry> AdvanceToNextDue()
    {
        _pending.RemoveAll(static r => r.IsCanceled);
        if (_pending.Count == 0)
        {
            return [];
        }

        var earliest = _pending[0].DueTime;
        foreach (var registration in _pending)
        {
            if (registration.DueTime < earliest)
            {
                earliest = registration.DueTime;
            }
        }

        return AdvanceTo(earliest);
    }

    public IReadOnlyList<ISimulationTimerEntry> AdvanceTo(TimeSpan target)
    {
        if (target < _now)
        {
            throw new ArgumentOutOfRangeException(nameof(target), "Simulation time cannot move backwards.");
        }

        _now = target;
        _pending.RemoveAll(static r => r.IsCanceled);
        var due = _pending
            .Where(registration => registration.DueTime <= target)
            .OrderBy(static registration => registration.DueTime)
            .ThenBy(static registration => registration.Sequence)
            .ToArray();
        if (due.Length > 0)
        {
            var dueSet = due.ToHashSet(ReferenceEqualityComparer.Instance);
            _pending.RemoveAll(dueSet.Contains);
        }

        return due;
    }

    /// <summary>
    /// Produces a deterministic, immutable snapshot of the live pending timeouts (due time, sequence,
    /// waiting operation), ordered by due time then sequence, for diagnostics and deadlock reports.
    /// </summary>
    public IReadOnlyList<SimulationPendingResourceTimeoutInfo> SnapshotPending()
    {
        var live = new List<SimulationResourceTimeoutRegistration>();
        foreach (var registration in _pending)
        {
            if (registration is SimulationResourceTimeoutRegistration resourceTimeout &&
                !resourceTimeout.IsCanceled)
            {
                live.Add(resourceTimeout);
            }
        }

        live.Sort(static (a, b) =>
        {
            var byTime = a.DueTime.CompareTo(b.DueTime);
            return byTime != 0 ? byTime : a.Sequence.CompareTo(b.Sequence);
        });

        var result = new List<SimulationPendingResourceTimeoutInfo>(live.Count);
        foreach (var registration in live)
        {
            result.Add(new SimulationPendingResourceTimeoutInfo(
                registration.DueTime,
                registration.Sequence,
                registration.Waiter.Operation.Id,
                registration.Waiter.Resource.Id));
        }

        return result;
    }

    public IReadOnlyList<SimulationPendingTimerInfo> SnapshotAllPending()
    {
        return _pending
            .Where(static timer =>
                !timer.IsCanceled
                && (timer is SimulationResourceTimeoutRegistration
                    || timer is SimulationTimerRegistration { DiagnosticKind: not null }))
            .OrderBy(static timer => timer.DueTime)
            .ThenBy(static timer => timer.Sequence)
            .Select(static timer => new SimulationPendingTimerInfo(
                timer.DueTime,
                timer.Sequence,
                timer is SimulationResourceTimeoutRegistration
                    ? "ResourceTimeout"
                    : ((SimulationTimerRegistration)timer).DiagnosticKind!))
            .ToArray();
    }

    /// <summary>Drops every pending timeout registration. Used during scheduler teardown.</summary>
    public void Clear() => _pending.Clear();

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"virtual-clock now={_now} pending={PendingCount}");
}

internal sealed record SimulationPendingTimerInfo(TimeSpan DueTime, long Sequence, string Kind);

/// <summary>
/// An immutable, deterministic snapshot of one pending virtual-time timeout, for diagnostics and
/// deadlock reports.
/// </summary>
/// <param name="DueTime">The virtual instant (offset from start) at which the timeout is due.</param>
/// <param name="Sequence">The deterministic registration order used to break ties at the same instant.</param>
/// <param name="OperationId">The operation whose wait this timeout will resolve.</param>
/// <param name="ResourceId">The resource the timed wait is parked on.</param>
public sealed record SimulationPendingResourceTimeoutInfo(
    TimeSpan DueTime,
    long Sequence,
    SimulationOperationId OperationId,
    SimulationResourceId ResourceId);

internal static class SimulationDeadlineMath
{
    public static TimeSpan SaturatingAdd(TimeSpan now, TimeSpan delay)
    {
        var remainingTicks = TimeSpan.MaxValue.Ticks - now.Ticks;
        return delay.Ticks > remainingTicks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks(now.Ticks + delay.Ticks);
    }
}
