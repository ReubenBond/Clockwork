using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Orleans.Testing.Simulation.Runtime.Execution;
using Orleans.Testing.Simulation.Runtime.Scheduling;

namespace Orleans.Testing.Simulation.Benchmarks;

[MemoryDiagnoser]
public class SimulationOperationWorkerBenchmarks
{
    public const int CallbackCount = 50_000;
    private int _initialCompletedCallbacks = 1;

    [Benchmark(OperationsPerInvoke = CallbackCount)]
    public int ShortCallbacks() => _initialCompletedCallbacks + Run(CallbackCount).CompletedCallbacks;

    public static SimulationOperationWorkerTrace Run(int callbackCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(callbackCount);

        using var scheduler = new SimulationScheduler(
            new SimulationRuntimeIdentity(Guid.Empty, Seed: 1, Description: "operation-worker-benchmark"));
        var threadIds = new HashSet<int>();
        var completed = 0;
        Action callback = () =>
        {
            threadIds.Add(Environment.CurrentManagedThreadId);
            completed++;
        };

        for (var index = 0; index < callbackCount; index++)
        {
            scheduler.Schedule("short-callback", callback);
        }

        var stopwatch = Stopwatch.StartNew();
        int dispatched = scheduler.Drain(CancellationToken.None);
        stopwatch.Stop();
        if (dispatched != callbackCount || completed != callbackCount)
        {
            throw new InvalidOperationException(
                $"Expected {callbackCount} callbacks but observed {dispatched} dispatches and {completed} completions.");
        }

        return new SimulationOperationWorkerTrace(
            callbackCount,
            completed,
            threadIds.Count,
            stopwatch.Elapsed);
    }
}

public readonly record struct SimulationOperationWorkerTrace(
    int CallbackCount,
    int CompletedCallbacks,
    int UniqueWorkerThreads,
    TimeSpan Elapsed);
