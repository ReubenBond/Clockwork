using BenchmarkDotNet.Attributes;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
public class ReplayRunnerBenchmarks
{
    private const int DispatchCount = 4096;
    private static readonly ReplayRecordingOptions s_recordingOptions = new()
    {
        RootSeed = 1,
        SchedulingPolicy = ReplaySchedulingPolicy.RoundRobin,
        MaxSteps = DispatchCount + 1,
    };

    private static readonly ReplayCompatibilityRequirements s_compatibility =
        ReplayCompatibilityRequirements.Current();

    private ReplayArtifact _artifact = null!;

    [Params(4, 64, 256)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void Setup() => _artifact = Record(OperationCount).Artifact;

    [Benchmark]
    public int RecordScenario() => Record(OperationCount).Steps;

    [Benchmark]
    public int ReplayScenario() => Replay(_artifact, OperationCount).Steps;

    public static int RunTrace(int iterationCount, int operationCount, bool replay)
    {
        ReplayArtifact? artifact = replay ? Record(operationCount).Artifact : null;
        var completedSteps = 0;
        for (var iteration = 0; iteration < iterationCount; iteration++)
        {
            completedSteps += replay
                ? Replay(artifact!, operationCount).Steps
                : Record(operationCount).Steps;
        }

        return completedSteps;
    }

    private static ReplayExecutionResult Record(int operationCount)
    {
        var scenario = new YieldingReplayScenario(operationCount, DispatchCount / operationCount);
        ReplayExecutionResult result = ReplayRunner.Record(
            s_recordingOptions,
            scenario.Schedule,
            CancellationToken.None);
        ValidateSteps(result);
        return result;
    }

    private static ReplayExecutionResult Replay(ReplayArtifact artifact, int operationCount)
    {
        var scenario = new YieldingReplayScenario(operationCount, DispatchCount / operationCount);
        ReplayExecutionResult result = ReplayRunner.Replay(
            artifact,
            s_compatibility,
            scenario.Schedule,
            maxSteps: DispatchCount + 1,
            cancellationToken: CancellationToken.None);
        ValidateSteps(result);
        return result;
    }

    private static void ValidateSteps(ReplayExecutionResult result)
    {
        if (result.Steps != DispatchCount)
        {
            throw new InvalidOperationException(
                $"Expected {DispatchCount} replay dispatches but observed {result.Steps}.");
        }
    }

    private sealed class YieldingReplayScenario(int operationCount, int stepsPerOperation)
    {
        private SimulationScheduler _scheduler = null!;

        public void Schedule(SimulationScheduler scheduler)
        {
            _scheduler = scheduler;
            Action body = Run;
            for (var operation = 0; operation < operationCount; operation++)
            {
                scheduler.Schedule("benchmark", body);
            }
        }

        private void Run()
        {
            for (var step = 0; step < stepsPerOperation; step++)
            {
                if (step + 1 < stepsPerOperation)
                {
                    _scheduler.Yield();
                }
            }
        }
    }
}
