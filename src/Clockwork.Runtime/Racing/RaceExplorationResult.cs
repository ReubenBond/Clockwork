namespace Clockwork.Runtime.Racing;

/// <summary>Classifies the race-specific outcome of a controlled scheduler run.</summary>
public enum RaceExplorationTerminationReason
{
    /// <summary>No conflicting unordered accesses were detected.</summary>
    CompletedWithoutRace,

    /// <summary>A conflicting unordered access was detected and reported.</summary>
    RaceDetected,
}

/// <summary>A structured race-exploration outcome, separate from operation exceptions.</summary>
/// <param name="Reason">The race-specific termination category.</param>
/// <param name="Race">The deterministic first-race report, when detected.</param>
public sealed record RaceExplorationResult(
    RaceExplorationTerminationReason Reason,
    RaceReport? Race)
{
    /// <summary>Gets a value indicating whether the run detected a race.</summary>
    public bool IsRaceDetected => Reason == RaceExplorationTerminationReason.RaceDetected;
}
