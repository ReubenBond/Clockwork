using BenchmarkDotNet.Running;
using Clockwork.Benchmarks;

if (args is ["--trace", var iterationCount]
    && int.TryParse(iterationCount, out var iterations)
    && iterations > 0)
{
    Console.WriteLine(DeterministicSchedulerBenchmarks.RunTrace(iterations));
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
