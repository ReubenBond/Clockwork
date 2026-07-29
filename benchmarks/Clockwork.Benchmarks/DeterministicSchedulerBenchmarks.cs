using BenchmarkDotNet.Attributes;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class DeterministicSchedulerBenchmarks
{
    private const int OperationCount = 4;
    private const int StepsPerOperation = 32;
    private const int SchedulingPointCount = OperationCount * StepsPerOperation;
    private int _initialCompletedSteps = 1;

    [Benchmark(Baseline = true, OperationsPerInvoke = SchedulingPointCount)]
    public int Direct()
    {
        var workload = new Workload(scheduler: null, _initialCompletedSteps);
        for (var operation = 0; operation < OperationCount; operation++)
        {
            workload.Run();
        }

        return workload.CompletedSteps;
    }

    [Benchmark(OperationsPerInvoke = SchedulingPointCount)]
    public int DeterministicScheduler() => RunScheduler(_initialCompletedSteps);

    public static int RunTrace(int iterationCount)
    {
        var completed = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            completed += RunScheduler(initialCompletedSteps: 0);
        }

        return completed;
    }

    private static int RunScheduler(int initialCompletedSteps)
    {
        using var scheduler = new SimulationScheduler(
            new SimulationRuntimeIdentity(Guid.Empty, Seed: 1, Description: "benchmark"));
        var workload = new Workload(scheduler, initialCompletedSteps);
        Action body = workload.Run;

        for (var operation = 0; operation < OperationCount; operation++)
        {
            scheduler.Schedule("benchmark", body);
        }

        int dispatched = scheduler.Drain();
        if (dispatched != SchedulingPointCount)
        {
            throw new InvalidOperationException(
                $"Expected {SchedulingPointCount} dispatches but observed {dispatched}.");
        }

        return workload.CompletedSteps;
    }

    private sealed class Workload(SimulationScheduler? scheduler, int initialCompletedSteps)
    {
        public int CompletedSteps { get; private set; } = initialCompletedSteps;

        public void Run()
        {
            for (var step = 0; step < StepsPerOperation; step++)
            {
                CompletedSteps++;
                if (step + 1 < StepsPerOperation)
                {
                    scheduler?.Yield();
                }
            }
        }
    }
}
