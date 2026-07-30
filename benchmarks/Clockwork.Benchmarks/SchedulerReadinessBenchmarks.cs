using BenchmarkDotNet.Attributes;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class SchedulerReadinessBenchmarks
{
    private const int DispatchCount = 256;

    [Params(0, 1, 16, 128)]
    public int PendingWaitCount { get; set; }

    [Benchmark(Baseline = true, OperationsPerInvoke = DispatchCount)]
    public int CreatedOperationsOnly() =>
        SchedulerBenchmarkWorkload.RunWithCreatedOperations(
            PendingWaitCount,
            DispatchCount,
            initialCompletedSteps: 1);

    [Benchmark(OperationsPerInvoke = DispatchCount)]
    public int PendingReadiness() =>
        SchedulerBenchmarkWorkload.RunWithPendingReadiness(
            PendingWaitCount,
            DispatchCount,
            initialCompletedSteps: 1);

    public static int RunTrace(int iterationCount, int pendingWaitCount)
    {
        var completed = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            completed += SchedulerBenchmarkWorkload.RunWithPendingReadiness(
                pendingWaitCount,
                DispatchCount,
                initialCompletedSteps: 0);
        }

        return completed;
    }
}
