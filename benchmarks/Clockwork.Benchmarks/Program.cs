using BenchmarkDotNet.Running;
using Clockwork.Benchmarks;

if (args is ["--trace", var iterationCount]
    && int.TryParse(iterationCount, out var iterations)
    && iterations > 0)
{
    Console.WriteLine(DeterministicSchedulerBenchmarks.RunTrace(iterations));
    return;
}

if (args is ["--trace-scaling", var scalingIterationCount, var operationCount]
    && int.TryParse(scalingIterationCount, out var scalingIterations)
    && scalingIterations > 0
    && int.TryParse(operationCount, out var operations)
    && operations > 0
    && 4096 % operations == 0)
{
    Console.WriteLine(SchedulerScalingBenchmarks.RunTrace(scalingIterations, operations));
    return;
}

if (args is ["--trace-readiness", var readinessIterationCount, var pendingWaitCount]
    && int.TryParse(readinessIterationCount, out var readinessIterations)
    && readinessIterations > 0
    && int.TryParse(pendingWaitCount, out var pendingWaits)
    && pendingWaits >= 0)
{
    Console.WriteLine(SchedulerReadinessBenchmarks.RunTrace(readinessIterations, pendingWaits));
    return;
}

if (args is ["--trace-replay", var replayIterationCount, var replayOperationCount, var replayMode]
    && int.TryParse(replayIterationCount, out var replayIterations)
    && replayIterations > 0
    && int.TryParse(replayOperationCount, out var replayOperations)
    && replayOperations > 0
    && 4096 % replayOperations == 0
    && replayMode is "record" or "replay")
{
    Console.WriteLine(
        ReplayRunnerBenchmarks.RunTrace(
            replayIterations,
            replayOperations,
            replay: replayMode == "replay"));
    return;
}

if (args is ["--trace-timers", var timerIterationCount, var timerCount, var timerMode]
    && int.TryParse(timerIterationCount, out var timerIterations)
    && timerIterations > 0
    && int.TryParse(timerCount, out var timers)
    && timers > 0
    && timerMode is "all" or "individual")
{
    Console.WriteLine(
        SimulationTimerQueueBenchmarks.RunTrace(
            timerIterations,
            timers,
            advanceIndividually: timerMode == "individual"));
    return;
}

if (args is ["--trace-decisions", var decisionIterationCount, var decisionMode]
    && int.TryParse(decisionIterationCount, out var decisionIterations)
    && decisionIterations > 0
    && decisionMode is "none" or "log")
{
    Console.WriteLine(
        SchedulerDecisionLogBenchmarks.RunTrace(
            decisionIterations,
            captureDecisions: decisionMode == "log"));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
