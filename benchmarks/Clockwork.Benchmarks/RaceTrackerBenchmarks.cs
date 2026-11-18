using BenchmarkDotNet.Attributes;
using System.Diagnostics.CodeAnalysis;
using Orleans.Testing.Simulation.Runtime.Execution;
using Orleans.Testing.Simulation.Runtime.Racing;
using Orleans.Testing.Simulation.Runtime.Scheduling;

namespace Orleans.Testing.Simulation.Benchmarks;

[MemoryDiagnoser]
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "BenchmarkDotNet invokes GlobalCleanup after each benchmark case.")]
public class RaceTrackerBenchmarks
{
    private static readonly RaceSourceLocation s_source =
        new("Benchmark.Write", 0, SourceFile: null, SourceLine: -1);

    private readonly object _section = new();
    private SimulationScheduler _scheduler = null!;
    private SimulationOperation _operation = null!;
    private RaceTracker _tracker = null!;
    private RaceTracker.LocationState _location = null!;

    // The components of the accessing operation's clock: its own, and one per other running operation it has synchronized with.
    [Params(1, 17)]
    public int ClockSize { get; set; }

    [Params(0, 4)]
    public int HeldSynchronizationCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scheduler = new SimulationScheduler(
            new SimulationRuntimeIdentity(Guid.Empty, Seed: 1, Description: "benchmark"));
        _tracker = new RaceTracker();
        _location = _tracker.ResolveLocation(RaceMemoryLocationKind.StaticField, null, "Benchmark::Value", null)!;
        _operation = Register();
        var signal = new object();
        for (var index = 1; index < ClockSize; index++)
        {
            _tracker.SignalSynchronization(Register(), signal);
        }

        _tracker.WaitSynchronization(_operation, signal);
        for (var index = 0; index < HeldSynchronizationCount; index++)
        {
            _tracker.EnterExclusive(_operation, new object());
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _scheduler.Dispose();

    [Benchmark]
    public void RecordRead() =>
        _tracker.RecordAccess(_operation, RaceAccessKind.Read, _location, s_source, Array.Empty<RaceSchedulingPoint>());

    [Benchmark]
    public void RecordWrite() =>
        _tracker.RecordAccess(_operation, RaceAccessKind.Write, _location, s_source, Array.Empty<RaceSchedulingPoint>());

    [Benchmark]
    public void LockSection()
    {
        _tracker.EnterExclusive(_operation, _section);
        _tracker.ExitExclusive(_operation, _section);
    }

    private SimulationOperation Register()
    {
        SimulationOperation operation = _scheduler.Register("benchmark", static () => { });
        _tracker.RegisterOperation(operation, parent: null);
        return operation;
    }
}
