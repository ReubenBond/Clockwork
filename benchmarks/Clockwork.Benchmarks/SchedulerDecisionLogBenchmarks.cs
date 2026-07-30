using BenchmarkDotNet.Attributes;
using Clockwork.Runtime.Decisions;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class SchedulerDecisionLogBenchmarks
{
    private const int OperationCount = 4;
    private const int DispatchCount = 4096;
    private const int StepsPerOperation = DispatchCount / OperationCount;
    private int _initialCompletedSteps = 1;

    [Benchmark(Baseline = true, OperationsPerInvoke = DispatchCount)]
    public int WithoutDecisionLog() =>
        SchedulerBenchmarkWorkload.RunScheduler(
            OperationCount,
            StepsPerOperation,
            _initialCompletedSteps);

    [Benchmark(OperationsPerInvoke = DispatchCount)]
    public int WithDecisionLog()
    {
        var log = new SimulationDecisionLog();
        int completed = SchedulerBenchmarkWorkload.RunScheduler(
            OperationCount,
            StepsPerOperation,
            _initialCompletedSteps,
            log);
        if (log.Records.Count != DispatchCount - 1)
        {
            throw new InvalidOperationException(
                $"Expected {DispatchCount - 1} scheduling decisions but observed {log.Records.Count}.");
        }

        return completed;
    }

    public static int RunTrace(int iterationCount, bool captureDecisions)
    {
        var completed = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            var log = captureDecisions ? new SimulationDecisionLog() : null;
            completed += SchedulerBenchmarkWorkload.RunScheduler(
                OperationCount,
                StepsPerOperation,
                initialCompletedSteps: 0,
                log);
        }

        return completed;
    }
}
