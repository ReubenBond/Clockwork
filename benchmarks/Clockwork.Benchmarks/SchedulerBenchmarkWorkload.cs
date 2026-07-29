using Clockwork.Runtime.Execution;
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
        int initialCompletedSteps)
    {
        using var scheduler = CreateScheduler();
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
        int initialCompletedSteps)
    {
        using var scheduler = CreateScheduler();
        var workload = new YieldingWorkload(
            scheduler,
            dispatchCount,
            initialCompletedSteps);

        // Register the runnable operation first so round-robin selection remains O(1). This isolates
        // the cost of polling the pending readiness set on every dispatch.
        scheduler.Schedule("benchmark", workload.Run);
        for (var wait = 0; wait < pendingWaitCount; wait++)
        {
            scheduler.ScheduleWhenReady(
                static () => false,
                static () => throw new InvalidOperationException("A pending readiness callback ran unexpectedly."));
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
