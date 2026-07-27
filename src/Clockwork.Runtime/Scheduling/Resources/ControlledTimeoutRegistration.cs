using System.Globalization;

namespace Clockwork.Runtime.Scheduling.Resources;

/// <summary>
/// A single pending virtual-time timeout owned by the scheduler's controlled virtual clock:
/// the due instant (in virtual time, as an offset from simulation start), a deterministic sequence
/// for tie-breaking coincident timeouts, and the waiter it will time out. Firing is driven purely by
/// virtual-time advancement - never by a real-time timer - so a timeout is a modeled, replayable
/// event, not wall-clock behavior.
/// </summary>
internal sealed class ControlledTimeoutRegistration
{
    internal ControlledTimeoutRegistration(TimeSpan dueTime, long sequence, ControlledResourceWaiter waiter)
    {
        DueTime = dueTime;
        Sequence = sequence;
        Waiter = waiter;
    }

    /// <summary>Gets the virtual-time instant (offset from start) at which this timeout is due.</summary>
    public TimeSpan DueTime { get; }

    /// <summary>
    /// Gets the deterministic, strictly increasing sequence used to break ties between timeouts due
    /// at the same virtual instant, so timeout firing order is a stable function of registration
    /// order.
    /// </summary>
    public long Sequence { get; }

    /// <summary>Gets the waiter this timeout resolves when it fires.</summary>
    public ControlledResourceWaiter Waiter { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this registration has been canceled (its waiter was
    /// resolved by a signal or cancellation first). A canceled registration is skipped when the
    /// clock fires due timeouts, and eventually removed from the pending set.
    /// </summary>
    public bool IsCanceled { get; set; }

    /// <inheritdoc />
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"timeout@{DueTime} seq={Sequence} -> {Waiter.Operation.Id}{(IsCanceled ? " (canceled)" : string.Empty)}");
}
