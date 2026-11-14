using System.Runtime.CompilerServices;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Strategies;
using Clockwork.Runtime.Tests.Scheduling;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>
/// No race verdict, race report, or scheduling decision depends on clock-slot identity or GC timing. One seeded run
/// collects garbage at every operation transition, so the detector sweeps the locations of collected objects and
/// numbers its slots differently; it reports the same race, reaches the same scheduling points, and schedules the same
/// transitions as the run which never forces a collection.
/// </summary>
public sealed class RaceDeterminismTests
{
    private const int Workers = 150;

    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(29)]
    public void ForcedCollectionsChangeNoOutput(int seed)
    {
        Outcome plain = Run(seed, collect: false);
        Outcome collected = Run(seed, collect: true);

        Assert.NotNull(plain.Race);
        Assert.Equal(plain.Race, collected.Race);
        Assert.Equal(plain.Points, collected.Points);
        Assert.Equal(plain.Transitions, collected.Transitions);

        // The collecting run swept the locations of collected objects, so its detector's state differed from a run
        // which keeps every location: one per worker's object, and the shared one.
        Assert.InRange(collected.Locations, 1, Workers);
    }

    private static Outcome Run(int seed, bool collect)
    {
        var listener = new Listener(collect);
        using SimulationScheduler scheduler = SchedulerTestHarness.NewScheduler(listener, seed);
        scheduler.SchedulingStrategy = SimulationSchedulingStrategies.SeededRandom(seed);
        for (int index = 0; index < Workers; index++)
        {
            int worker = index;
            scheduler.Schedule("worker", () => Work(worker));
        }

        scheduler.Drain(TestContext.Current.CancellationToken);
        return new Outcome(
            scheduler.FirstRace?.ToDetailedString(),
            string.Join('\n', scheduler.CaptureRaceSchedulingPoints()),
            string.Join(',', listener.Transitions),
            scheduler.RaceTrackerStatistics.Locations);
    }

    // Each worker writes a fresh object and reads the shared location; one in fifty also writes it.
    private static void Work(int worker)
    {
        WriteFreshObject(worker);
        RaceInstrumentation.ReadStatic("Fx.State::Shared", "worker", 2, null, -1);
        if (worker % 50 == 49)
        {
            RaceInstrumentation.WriteStatic("Fx.State::Shared", "worker", 3, null, -1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteFreshObject(int worker) =>
        RaceInstrumentation.WriteInstance(new int[worker % 3 + 1], "Fx.State::Field", "worker", 1, null, -1);

    private sealed record Outcome(string? Race, string Points, string Transitions, int Locations);

    private sealed class Listener(bool collect) : ISimulationOperationListener
    {
        public List<string> Transitions { get; } = [];

        public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState)
        {
            lock (Transitions)
            {
                Transitions.Add($"{operation.Id.Value}:{newState}");
            }

            if (collect)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
    }
}
