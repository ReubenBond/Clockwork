using BenchmarkDotNet.Attributes;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class SchedulerAllocationBenchmarks
{
    private const int OperationCount = 4;

    [Params(2, 32, 512, 4096)]
    public int StepsPerOperation { get; set; }

    [Benchmark]
    public int DeterministicScheduler() =>
        SchedulerBenchmarkWorkload.RunScheduler(
            OperationCount,
            StepsPerOperation,
            initialCompletedSteps: 1);
}
