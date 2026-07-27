namespace Clockwork;

/// <summary>
/// Adaptive execution-budget entry points for <see cref="SimulationCluster{TNode}"/>: escalating
/// counterparts to <see cref="SimulationCluster{TNode}.RunUntil(Func{bool}, int)"/> and
/// <see cref="SimulationCluster{TNode}.RunUntilIdle(TimeSpan?, int)"/> that spare callers from
/// picking a size-specific <c>maxIterations</c> value.
/// </summary>
public abstract partial class SimulationCluster<TNode>
    where TNode : SimulationNode
{
    /// <summary>
    /// <para>
    /// Runs the simulation until <paramref name="condition"/> becomes true, automatically
    /// escalating the iteration budget across successive batches instead of requiring a single
    /// <c>maxIterations</c> value sized to the scenario. This is the adaptive counterpart to
    /// <see cref="RunUntil(Func{bool}, int)"/>/<see cref="RunUntilDetailed(Func{bool}, int)"/>.
    /// </para>
    /// <para>
    /// <b>Progress heuristic:</b> each batch is run with <see cref="RunUntilDetailed(Func{bool}, int)"/>.
    /// If a batch stops for any reason other than <see cref="SimulationExecutionReason.MaxIterationsReached"/>,
    /// execution stops immediately and that result is returned - the goal was met
    /// (<see cref="SimulationExecutionReason.ConditionMet"/>), or the simulation is genuinely stuck
    /// in a way a bigger iteration budget cannot fix (<see cref="SimulationExecutionReason.Idle"/>,
    /// <see cref="SimulationExecutionReason.IdleWithPendingWork"/>,
    /// <see cref="SimulationExecutionReason.MaxSimulatedTimeAdvanceExceeded"/>,
    /// <see cref="SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded"/>, or
    /// <see cref="SimulationExecutionReason.TeardownCancellationRequested"/>). Escalation only
    /// happens after <see cref="SimulationExecutionReason.MaxIterationsReached"/>, because reaching
    /// that reason means every one of the batch's iterations executed a real step or a clock
    /// advance (<c>StepsExecuted + TimeAdvanceCount == Iterations</c> for that batch) - there is
    /// always more forward motion to give a bigger budget a chance to reach, and a scenario that is
    /// truly spinning without making progress is instead caught by the separate, always-enforced
    /// <see cref="MaxConsecutiveTimeAdvances"/> safety net. Its consecutive-advance count is carried
    /// across batch boundaries and surfaces as
    /// <see cref="SimulationExecutionReason.MaxConsecutiveTimeAdvancesExceeded"/> rather than
    /// <see cref="SimulationExecutionReason.MaxIterationsReached"/>.
    /// </para>
    /// <para>
    /// Escalation never removes the hard safety cap that explicit <c>maxIterations</c> limits
    /// already provide: the total number of iterations across every batch cannot exceed
    /// <paramref name="budget"/>'s <see cref="SimulationAdaptiveBudget.MaxTotalIterations"/>
    /// (<see cref="SimulationAdaptiveBudget.Default"/> if omitted). If that ceiling is reached
    /// while still needing more iterations, the returned result's
    /// <see cref="SimulationExecutionResult.Reason"/> is
    /// <see cref="SimulationExecutionReason.MaxIterationsReached"/>, exactly as it would be for a
    /// plain <see cref="RunUntilDetailed(Func{bool}, int)"/> call whose fixed budget ran out. The
    /// returned <see cref="SimulationExecutionResult.Limits"/> reports this overall adaptive ceiling,
    /// not the size of the final batch.
    /// </para>
    /// </summary>
    /// <param name="condition">The condition that ends the run when it becomes true.</param>
    /// <param name="budget">
    /// The adaptive budget controlling the initial batch size, escalation factor, and hard total
    /// iteration ceiling. Defaults to <see cref="SimulationAdaptiveBudget.Default"/>.
    /// </param>
    /// <returns>A detailed result describing the whole (possibly multi-batch) execution.</returns>
    public SimulationExecutionResult RunUntilConverged(Func<bool> condition, SimulationAdaptiveBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        using var _ = Guard.Enter();
        return RunConvergedCore(
            budget ?? SimulationAdaptiveBudget.Default,
            (batchMaxIterations, consecutiveTimeAdvances) => ExecuteDriveLoop(
                condition,
                MaxSimulatedTimeAdvance,
                batchMaxIterations,
                observeTeardownCancellation: false,
                initialConsecutiveTimeAdvances: consecutiveTimeAdvances));
    }

    /// <summary>
    /// <para>
    /// Runs the simulation until it becomes idle, automatically escalating the iteration budget
    /// across successive batches instead of requiring a single <c>maxIterations</c> value sized to
    /// the scenario. This is the adaptive counterpart to
    /// <see cref="RunUntilIdle(TimeSpan?, int)"/>/<see cref="RunUntilIdleDetailed(TimeSpan?, int)"/>.
    /// </para>
    /// <para>
    /// Follows the same progress heuristic and hard-cap behavior as
    /// <see cref="RunUntilConverged(Func{bool}, SimulationAdaptiveBudget?)"/> - see its remarks for
    /// the precise rules governing when escalation happens and when it stops.
    /// </para>
    /// </summary>
    /// <param name="maxSimulatedTime">The maximum simulated-time gap to jump in a single advance. Defaults to <see cref="MaxSimulatedTimeAdvance"/>.</param>
    /// <param name="budget">
    /// The adaptive budget controlling the initial batch size, escalation factor, and hard total
    /// iteration ceiling. Defaults to <see cref="SimulationAdaptiveBudget.Default"/>.
    /// </param>
    /// <returns>A detailed result describing the whole (possibly multi-batch) execution.</returns>
    public SimulationExecutionResult RunUntilIdleConverged(TimeSpan? maxSimulatedTime = null, SimulationAdaptiveBudget? budget = null)
    {
        using var _ = Guard.Enter();
        return RunConvergedCore(
            budget ?? SimulationAdaptiveBudget.Default,
            (batchMaxIterations, consecutiveTimeAdvances) => ExecuteDriveLoop(
                condition: null,
                maxSimulatedTime ?? MaxSimulatedTimeAdvance,
                batchMaxIterations,
                observeTeardownCancellation: true,
                initialConsecutiveTimeAdvances: consecutiveTimeAdvances));
    }

    /// <summary>
    /// Shared escalation loop for <see cref="RunUntilConverged"/> and <see cref="RunUntilIdleConverged"/>.
    /// Runs <paramref name="runBatch"/> repeatedly with an escalating iteration budget, combining
    /// results, until a batch stops for a reason other than
    /// <see cref="SimulationExecutionReason.MaxIterationsReached"/> or the hard
    /// <see cref="SimulationAdaptiveBudget.MaxTotalIterations"/> cap is reached.
    /// </summary>
    private SimulationExecutionResult RunConvergedCore(SimulationAdaptiveBudget budget, Func<int, int, SimulationExecutionResult> runBatch)
    {
        var startTime = TimeProvider.GetUtcNow();
        var totalIterations = 0;
        var currentBatchSize = budget.InitialMaxIterations;
        SimulationExecutionResult? combined = null;

        while (true)
        {
            var remaining = budget.MaxTotalIterations - totalIterations;
            var batchMaxIterations = Math.Min(currentBatchSize, remaining);
            var batch = runBatch(batchMaxIterations, combined?.ConsecutiveTimeAdvanceCount ?? 0);

            combined = combined is null ? batch : CombineExecutionResults(startTime, combined, batch, forcedTimeAdvanceCount: 0);
            combined = WithOverallAdaptiveLimits(combined, budget.MaxTotalIterations);
            totalIterations += batch.Iterations;

            if (batch.Reason != SimulationExecutionReason.MaxIterationsReached || totalIterations >= budget.MaxTotalIterations)
            {
                return combined;
            }

            var nextBatchSize = currentBatchSize * budget.GrowthFactor;
            currentBatchSize = nextBatchSize >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(nextBatchSize);
        }
    }

    private static SimulationExecutionResult WithOverallAdaptiveLimits(SimulationExecutionResult result, int maxTotalIterations) =>
        new(
            result.Reason,
            result.StartTime,
            result.EndTime,
            result.Iterations,
            result.StepsExecuted,
            result.TimeAdvanceCount,
            result.ConsecutiveTimeAdvanceCount,
            result.PendingWork,
            new SimulationExecutionLimits(
                maxTotalIterations,
                result.Limits.MaxSimulatedTimeAdvance,
                result.Limits.MaxConsecutiveTimeAdvances),
            result.AttemptedTimeAdvance);
}
