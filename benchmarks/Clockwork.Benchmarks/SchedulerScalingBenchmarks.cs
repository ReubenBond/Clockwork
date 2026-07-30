using BenchmarkDotNet.Attributes;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class SchedulerScalingBenchmarks
{
    private const int DispatchCount = 4096;

    [Params(1, 4, 16, 64, 256)]
    public int OperationCount { get; set; }

    [Benchmark(OperationsPerInvoke = DispatchCount)]
    public int DeterministicScheduler()
    {
        var stepsPerOperation = DispatchCount / OperationCount;
        return SchedulerBenchmarkWorkload.RunScheduler(
            OperationCount,
            stepsPerOperation,
            initialCompletedSteps: 1);
    }

    public static int RunTrace(int iterationCount, int operationCount)
    {
        var completed = 0;
        var stepsPerOperation = DispatchCount / operationCount;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            completed += SchedulerBenchmarkWorkload.RunScheduler(
                operationCount,
                stepsPerOperation,
                initialCompletedSteps: 0);
        }

        return completed;
    }
}
