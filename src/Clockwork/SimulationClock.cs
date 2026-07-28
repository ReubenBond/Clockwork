using System.Diagnostics;
using System.Globalization;
using Clockwork.Runtime.Scheduling;

namespace Clockwork;

/// <summary>Read-only clock facade over a simulation scheduler's single virtual timeline.</summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SimulationClock
{
    private readonly SimulationScheduler _scheduler;

    internal SimulationClock(SimulationScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
    }

    /// <summary>Gets the simulation's fixed virtual-time origin.</summary>
    public DateTimeOffset StartDateTime => _scheduler.StartDateTime;

    /// <summary>Gets elapsed virtual time since <see cref="StartDateTime"/>.</summary>
    public TimeSpan CurrentTime => _scheduler.VirtualTime;

    /// <summary>Gets the current virtual UTC instant.</summary>
    public DateTimeOffset UtcNow => _scheduler.UtcNow;

    internal void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        _scheduler.AdvanceVirtualTimeTo(CurrentTime + delta);
    }

    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"SimulationClock({CurrentTime:hh\\:mm\\:ss\\.fff})");
}
