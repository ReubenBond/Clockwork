namespace Clockwork;

/// <summary>
/// Options for a single execution of <see cref="SimulationDriveLoop"/>.
/// Consolidates the parameters that were previously duplicated across
/// <c>RunUntil</c>, <c>RunUntilIdle</c>, and <c>RunForDuration</c> on
/// <see cref="SimulationCluster{TNode}"/>.
/// </summary>
/// <param name="Condition">
/// The condition that ends the loop successfully, or <see langword="null"/> to run until idle
/// (i.e. there is no condition to satisfy - the loop only stops for idle/limit/cancellation reasons).
/// </param>
/// <param name="MaxSimulatedTimeAdvance">The maximum simulated-time gap to jump across in a single advance.</param>
/// <param name="MaxIterations">The maximum number of loop iterations to execute.</param>
/// <param name="MaxConsecutiveTimeAdvances">The maximum number of consecutive time advances without executed work.</param>
/// <param name="ObserveTeardownCancellation">Whether to check the teardown cancellation token each iteration.</param>
internal readonly record struct SimulationDriveLoopOptions(
    Func<bool>? Condition,
    TimeSpan MaxSimulatedTimeAdvance,
    int MaxIterations,
    int MaxConsecutiveTimeAdvances,
    bool ObserveTeardownCancellation);

/// <summary>
/// <para>
/// The single internal execution engine that drives a <see cref="SimulationCluster{TNode}"/>
/// forward in time. This consolidates the loop logic that was previously duplicated across
/// <c>RunUntilCore</c> and <c>RunUntilIdleCore</c>: round-robin task execution, time advancement
/// to the next scheduled item, and all stuck/limit detection.
/// </para>
/// <para>
/// The engine is deliberately decoupled from <see cref="SimulationCluster{TNode}"/> via
/// delegates so that its state machine can be reasoned about (and, if needed, tested)
/// independently of the generic cluster type and node registry.
/// </para>
/// </summary>
/// <param name="getUtcNow">Returns the current simulated time.</param>
/// <param name="runOneTaskRoundRobin">Attempts to execute one ready task; returns true if one ran.</param>
/// <param name="getNextWaitingDueTime">Returns the earliest due time of any not-yet-ready item, or null.</param>
/// <param name="advanceClock">Advances the shared simulation clock by the given, non-negative amount.</param>
/// <param name="capturePendingWorkSummary">Captures a snapshot of runnable/waiting/blocked work.</param>
/// <param name="teardownCancellationToken">The cluster's teardown cancellation token.</param>
internal sealed class SimulationDriveLoop(
    Func<DateTimeOffset> getUtcNow,
    Func<bool> runOneTaskRoundRobin,
    Func<DateTimeOffset?> getNextWaitingDueTime,
    Action<TimeSpan> advanceClock,
    Func<SimulationPendingWorkSummary> capturePendingWorkSummary,
    CancellationToken teardownCancellationToken)
{
    /// <summary>
    /// Runs the drive loop to completion for a single logical operation (one of the RunUntil/RunUntilIdle
    /// family of calls), returning a structured result describing exactly why it stopped.
    /// </summary>
    /// <param name="options">The options controlling this execution.</param>
    /// <returns>A structured result describing the execution.</returns>
    public SimulationExecutionResult Execute(SimulationDriveLoopOptions options)
    {
        var startTime = getUtcNow();
        var consecutiveTimeAdvances = 0;
        var totalTimeAdvances = 0;
        var stepsExecuted = 0;

        for (var iteration = 0; iteration < options.MaxIterations; iteration++)
        {
            if (options.ObserveTeardownCancellation && teardownCancellationToken.IsCancellationRequested)
            {
                return Complete(SimulationExecutionReason.TeardownCancellationRequested, iteration);
            }

            if (options.Condition is { } condition && condition())
            {
                return Complete(SimulationExecutionReason.ConditionMet, iteration);
            }

            if (runOneTaskRoundRobin())
            {
                stepsExecuted++;
                consecutiveTimeAdvances = 0; // Reset the stuck-detection counter when real work happens.
                continue;
            }

            // No tasks ready to execute right now - need to advance time or stop.
            var nextDueTime = getNextWaitingDueTime();
            if (nextDueTime is null)
            {
                // Nothing is waiting for a future time either. Distinguish "truly nothing left" from
                // "there is work, but it cannot run" (e.g. a ready item on a suspended node's queue).
                var pendingWork = capturePendingWorkSummary();
                var idleReason = pendingWork.BlockedCount > 0
                    ? SimulationExecutionReason.IdleWithPendingWork
                    : SimulationExecutionReason.Idle;
                return Complete(idleReason, iteration, pendingWork);
            }

            var timeDelta = nextDueTime.Value - getUtcNow();
            if (timeDelta > options.MaxSimulatedTimeAdvance)
            {
                return Complete(SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded, iteration, attemptedTimeAdvance: timeDelta);
            }

            if (timeDelta > TimeSpan.Zero)
            {
                advanceClock(timeDelta);
            }

            consecutiveTimeAdvances++;
            totalTimeAdvances++;

            if (consecutiveTimeAdvances > options.MaxConsecutiveTimeAdvances)
            {
                return Complete(SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded, iteration);
            }
        }

        return Complete(SimulationExecutionReason.MaxIterationsReached, options.MaxIterations);

        SimulationExecutionResult Complete(
            SimulationExecutionReason reason,
            int iteration,
            SimulationPendingWorkSummary? pendingWork = null,
            TimeSpan? attemptedTimeAdvance = null)
        {
            return new SimulationExecutionResult(
                reason,
                startTime,
                getUtcNow(),
                iteration,
                stepsExecuted,
                totalTimeAdvances,
                consecutiveTimeAdvances,
                pendingWork ?? capturePendingWorkSummary(),
                new SimulationExecutionLimits(options.MaxIterations, options.MaxSimulatedTimeAdvance, options.MaxConsecutiveTimeAdvances),
                attemptedTimeAdvance);
        }
    }
}
