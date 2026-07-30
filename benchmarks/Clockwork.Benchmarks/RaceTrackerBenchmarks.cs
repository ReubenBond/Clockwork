using BenchmarkDotNet.Attributes;
using System.Diagnostics.CodeAnalysis;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Benchmarks;

[MemoryDiagnoser]
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet invokes GlobalCleanup after each benchmark case.")]
public class RaceTrackerBenchmarks
{
    private static readonly RaceMemoryLocation s_location =
        new(RaceMemoryLocationKind.StaticField, 0, "Benchmark::Value");

    private static readonly RaceSourceLocation s_source =
        new("Benchmark.Write", 0, SourceFile: null, SourceLine: -1);

    private SimulationScheduler _scheduler = null!;
    private SimulationOperation _operation = null!;
    private RaceTracker _tracker = null!;

    [Params(0, 4, 16)]
    public int HeldSynchronizationCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scheduler = new SimulationScheduler(
            new SimulationRuntimeIdentity(Guid.Empty, Seed: 1, Description: "benchmark"));
        _operation = _scheduler.Register("benchmark", static () => { });
        _tracker = new RaceTracker();
        _tracker.RegisterOperation(_operation, parent: null);
        for (var index = 0; index < HeldSynchronizationCount; index++)
        {
            _tracker.EnterSynchronization(_operation, new object());
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _scheduler.Dispose();

    [Benchmark]
    public void RecordWrite() =>
        _tracker.RecordAccess(
            _operation,
            RaceAccessKind.Write,
            s_location,
            s_source,
            Array.Empty<RaceSchedulingPoint>());
}
