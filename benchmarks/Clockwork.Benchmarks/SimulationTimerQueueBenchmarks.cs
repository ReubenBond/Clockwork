using BenchmarkDotNet.Attributes;
using Clockwork.Runtime.Scheduling.Resources;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class SimulationTimerQueueBenchmarks
{
    private int _initialAdvanced = 1;

    [Benchmark]
    [Arguments(32)]
    [Arguments(1024)]
    [Arguments(16384)]
    public int AdvanceAllAtOnce(int timerCount) =>
        _initialAdvanced + RunAdvanceAllAtOnce(timerCount);

    [Benchmark]
    [Arguments(32)]
    [Arguments(256)]
    [Arguments(1024)]
    public int AdvanceIndividually(int timerCount) =>
        _initialAdvanced + RunAdvanceIndividually(timerCount);

    public static int RunTrace(int iterationCount, int timerCount, bool advanceIndividually)
    {
        var advanced = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            advanced += advanceIndividually
                ? RunAdvanceIndividually(timerCount)
                : RunAdvanceAllAtOnce(timerCount);
        }

        return advanced;
    }

    private static int RunAdvanceAllAtOnce(int timerCount)
    {
        var queue = new SimulationTimerQueue();
        for (var timer = timerCount; timer > 0; timer--)
        {
            queue.Schedule(TimeSpan.FromTicks(timer), onElapsed: null);
        }

        IReadOnlyList<ISimulationTimerEntry> due = queue.AdvanceTo(TimeSpan.MaxValue);
        ValidateCount(timerCount, due.Count);
        return due.Count;
    }

    private static int RunAdvanceIndividually(int timerCount)
    {
        var queue = new SimulationTimerQueue();
        for (var timer = 1; timer <= timerCount; timer++)
        {
            queue.Schedule(TimeSpan.FromTicks(timer), onElapsed: null);
        }

        var advanced = 0;
        while (queue.HasPending)
        {
            advanced += queue.AdvanceToNextDue().Count;
        }

        ValidateCount(timerCount, advanced);
        return advanced;
    }

    private static void ValidateCount(int expected, int actual)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected} timers to advance but observed {actual}.");
        }
    }
}
