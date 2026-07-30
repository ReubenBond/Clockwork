using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Benchmarks;

internal static class SchedulerBenchmarkWorkload
{
    public static int RunDirect(
        int operationCount,
        int stepsPerOperation,
        int initialCompletedSteps)
    {
        var workload = new YieldingWorkload(
            scheduler: null,
            stepsPerOperation,
            initialCompletedSteps);
        for (var operation = 0; operation < operationCount; operation++)
        {
            workload.Run();
        }

        return workload.CompletedSteps;
    }

    public static int RunScheduler(
        int operationCount,
        int stepsPerOperation,
        int initialCompletedSteps,
        ISimulationDecisionLog? decisionLog = null)
    {
        using var scheduler = CreateScheduler();
        scheduler.DecisionLog = decisionLog;
        var workload = new YieldingWorkload(
            scheduler,
            stepsPerOperation,
            initialCompletedSteps);
        Action body = workload.Run;

        for (var operation = 0; operation < operationCount; operation++)
        {
            scheduler.Schedule("benchmark", body);
        }

        var expectedDispatches = operationCount * stepsPerOperation;
        var dispatched = scheduler.Drain(CancellationToken.None);
        if (dispatched != expectedDispatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedDispatches} dispatches but observed {dispatched}.");
        }

        return workload.CompletedSteps;
    }

    public static int RunWithPendingReadiness(
        int pendingWaitCount,
        int dispatchCount,
        int initialCompletedSteps) =>
        RunWithPendingOperations(
            pendingWaitCount,
            dispatchCount,
            initialCompletedSteps,
            useReadinessWaits: true);

    public static int RunWithCreatedOperations(
        int pendingOperationCount,
        int dispatchCount,
        int initialCompletedSteps) =>
        RunWithPendingOperations(
            pendingOperationCount,
            dispatchCount,
            initialCompletedSteps,
            useReadinessWaits: false);

    private static int RunWithPendingOperations(
        int pendingOperationCount,
        int dispatchCount,
        int initialCompletedSteps,
        bool useReadinessWaits)
    {
        using var scheduler = CreateScheduler();
        var workload = new YieldingWorkload(
            scheduler,
            dispatchCount,
            initialCompletedSteps);

        // Register the runnable operation first so both benchmark variants have identical selection order.
        scheduler.Schedule("benchmark", workload.Run);
        for (var operation = 0; operation < pendingOperationCount; operation++)
        {
            if (useReadinessWaits)
            {
                scheduler.ScheduleWhenReady(
                    static () => false,
                    static () => throw new InvalidOperationException("A pending readiness callback ran unexpectedly."));
            }
            else
            {
                scheduler.Register("pending", static () => { });
            }
        }

        var dispatched = scheduler.Drain(CancellationToken.None);
        if (dispatched != dispatchCount)
        {
            throw new InvalidOperationException(
                $"Expected {dispatchCount} dispatches but observed {dispatched}.");
        }

        return workload.CompletedSteps;
    }

    private static SimulationScheduler CreateScheduler() =>
        new(new SimulationRuntimeIdentity(Guid.Empty, Seed: 1, Description: "benchmark"));

    private sealed class YieldingWorkload(
        SimulationScheduler? scheduler,
        int stepsPerOperation,
        int initialCompletedSteps)
    {
        public int CompletedSteps { get; private set; } = initialCompletedSteps;

        public void Run()
        {
            for (var step = 0; step < stepsPerOperation; step++)
            {
                CompletedSteps++;
                if (step + 1 < stepsPerOperation)
                {
                    scheduler?.Yield();
                }
            }
        }
    }
}
