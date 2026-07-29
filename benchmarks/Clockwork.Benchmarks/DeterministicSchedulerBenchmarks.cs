using BenchmarkDotNet.Attributes;
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
        => SchedulerBenchmarkWorkload.RunDirect(
            OperationCount,
            StepsPerOperation,
            _initialCompletedSteps);

    [Benchmark(OperationsPerInvoke = SchedulingPointCount)]
    public int DeterministicScheduler() =>
        SchedulerBenchmarkWorkload.RunScheduler(
            OperationCount,
            StepsPerOperation,
            _initialCompletedSteps);

    public static int RunTrace(int iterationCount)
    {
        var completed = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            completed += SchedulerBenchmarkWorkload.RunScheduler(
                OperationCount,
                StepsPerOperation,
                initialCompletedSteps: 0);
        }

        return completed;
    }
}
