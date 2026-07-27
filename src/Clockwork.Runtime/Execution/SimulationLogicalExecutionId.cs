namespace Clockwork.Runtime.Execution;

/// <summary>
/// <para>
/// A placeholder for the current "logical execution" identity - the unit of scheduling that a
/// future controlled-operation scheduler (Phase 3) will assign to each independently-interleavable
/// strand of simulated work (roughly: one simulated "fiber"/task chain). Phase 2 does not implement
/// a scheduler, so this type carries no scheduling semantics yet; it exists so that
/// <see cref="SimulationExecutionContext"/> and the decision-log model
/// (<see cref="Clockwork.Runtime.Decisions.SimulationDecisionRecord"/>) have a stable slot to record
/// against today, without churn when the real scheduler lands.
/// </para>
/// <para>
/// <see cref="None"/> is the value observed outside any explicitly-entered logical execution scope.
/// <see cref="SimulationLogicalExecutionIdSource"/> hands out fresh, monotonically increasing values
/// within one runtime - it is not a scheduler and makes no ordering guarantee beyond uniqueness.
/// </para>
/// </summary>
/// <param name="Value">The opaque numeric identity. Only meaningful for equality comparisons.</param>
public readonly record struct SimulationLogicalExecutionId(long Value)
{
    /// <summary>
    /// Gets the value observed outside any explicitly-entered logical execution scope.
    /// </summary>
    public static SimulationLogicalExecutionId None => default;

    /// <summary>
    /// Gets a value indicating whether this is <see cref="None"/>.
    /// </summary>
    public bool IsNone => Value == 0;
}

/// <summary>
/// Hands out fresh, process-unique <see cref="SimulationLogicalExecutionId"/> values. This is a
/// placeholder identity generator, not a scheduler: it makes no interleaving or ordering
/// guarantee beyond "every call returns a value distinct from every other call on this instance".
/// </summary>
public sealed class SimulationLogicalExecutionIdSource
{
    private long _next;

    /// <summary>
    /// Returns the next fresh <see cref="SimulationLogicalExecutionId"/> from this source.
    /// </summary>
    public SimulationLogicalExecutionId Next() => new(Interlocked.Increment(ref _next));
}
