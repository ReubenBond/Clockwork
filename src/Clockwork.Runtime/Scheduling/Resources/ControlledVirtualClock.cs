using System.Globalization;

namespace Clockwork.Runtime.Scheduling.Resources;

/// <summary>
/// <para>
/// The scheduler's in-runtime source of modeled (virtual) time and the ordered set of pending
/// timeout registrations that fire as time advances. It deliberately mirrors the semantics of the
/// root <c>SimulationClock</c>/<c>SimulationTaskQueue</c> pair - a monotonic <see cref="Now"/> offset
/// from simulation start plus a due-time-ordered queue - but lives entirely inside
/// <c>Clockwork.Runtime</c>.
/// </para>
/// <para>
/// <b>Why a private clock instead of reusing <c>SimulationClock</c>?</b> <c>Clockwork.Runtime</c> has
/// no project reference to the root <c>Clockwork</c> assembly (the dependency runs the other way, so
/// the runtime stays a leaf that the host builds on). Reaching up to <c>SimulationClock</c> would
/// invert that layering and create a cycle. Modeling virtual time here keeps the scheduler
/// self-contained; a host that owns a real <c>SimulationClock</c> can drive this clock in lockstep.
/// </para>
/// <para>
/// <b>Threading.</b> This type performs no locking of its own. The owning
/// <see cref="ControlledOperationScheduler"/> only ever touches it while holding its scheduler gate,
/// so every read and mutation is already serialized. Timeouts fire <i>only</i> when the scheduler
/// explicitly advances time (which it does only when no operation is runnable), never from a
/// real-time timer, which is what makes a timeout a deterministic, replayable event.
/// </para>
/// </summary>
internal sealed class ControlledVirtualClock
{
    private readonly List<ControlledTimeoutRegistration> _pending = [];
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
    /// <returns>The created registration, already linked to the waiter's <see cref="ControlledResourceWaiter.Timeout"/> by the caller.</returns>
    public ControlledTimeoutRegistration Schedule(TimeSpan delay, ControlledResourceWaiter waiter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        var registration = new ControlledTimeoutRegistration(
            ControlledDeadlineMath.SaturatingAdd(_now, delay),
            ++_nextSequence,
            waiter);
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
    public IReadOnlyList<ControlledTimeoutRegistration> AdvanceToNextDue()
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

        // Time only ever moves forward; the earliest pending due time is always >= Now.
        _now = earliest;

        var due = new List<ControlledTimeoutRegistration>();
        foreach (var registration in _pending)
        {
            if (registration.DueTime == earliest)
            {
                due.Add(registration);
            }
        }

        due.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
        _pending.RemoveAll(r => r.DueTime == earliest);
        return due;
    }

    /// <summary>
    /// Produces a deterministic, immutable snapshot of the live pending timeouts (due time, sequence,
    /// waiting operation), ordered by due time then sequence, for diagnostics and deadlock reports.
    /// </summary>
    public IReadOnlyList<ControlledPendingTimeoutInfo> SnapshotPending()
    {
        var live = new List<ControlledTimeoutRegistration>();
        foreach (var registration in _pending)
        {
            if (!registration.IsCanceled)
            {
                live.Add(registration);
            }
        }

        live.Sort(static (a, b) =>
        {
            var byTime = a.DueTime.CompareTo(b.DueTime);
            return byTime != 0 ? byTime : a.Sequence.CompareTo(b.Sequence);
        });

        var result = new List<ControlledPendingTimeoutInfo>(live.Count);
        foreach (var registration in live)
        {
            result.Add(new ControlledPendingTimeoutInfo(
                registration.DueTime,
                registration.Sequence,
                registration.Waiter.Operation.Id,
                registration.Waiter.Resource.Id));
        }

        return result;
    }

    /// <summary>Drops every pending timeout registration. Used during scheduler teardown.</summary>
    public void Clear() => _pending.Clear();

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"virtual-clock now={_now} pending={PendingCount}");
}

/// <summary>
/// An immutable, deterministic snapshot of one pending virtual-time timeout, for diagnostics and
/// deadlock reports.
/// </summary>
/// <param name="DueTime">The virtual instant (offset from start) at which the timeout is due.</param>
/// <param name="Sequence">The deterministic registration order used to break ties at the same instant.</param>
/// <param name="OperationId">The operation whose wait this timeout will resolve.</param>
/// <param name="ResourceId">The resource the timed wait is parked on.</param>
public sealed record ControlledPendingTimeoutInfo(
    TimeSpan DueTime,
    long Sequence,
    ControlledOperationId OperationId,
    ControlledResourceId ResourceId);

internal static class ControlledDeadlineMath
{
    public static TimeSpan SaturatingAdd(TimeSpan now, TimeSpan delay)
    {
        var remainingTicks = TimeSpan.MaxValue.Ticks - now.Ticks;
        return delay.Ticks > remainingTicks
            ? TimeSpan.MaxValue
            : TimeSpan.FromTicks(now.Ticks + delay.Ticks);
    }
}
