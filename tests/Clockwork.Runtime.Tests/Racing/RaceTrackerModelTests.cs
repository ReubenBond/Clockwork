using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Tests.Scheduling;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>
/// Model-based check of the race detector's clocks. Seeded random programs of forks, continuations, root operations,
/// accesses, lock sections, signals and waits, resource releases, and terminations drive the detector and a reference
/// which keeps one clock component per operation, compares whole clocks, and never drops state. Both report their
/// first race at the same access, and the earlier access the detector names is one the reference finds racing it.
/// Lock sections are atomic, as a controlled lock's are, since a held lock suppresses the scheduling points inside it.
/// </summary>
public sealed class RaceTrackerModelTests
{
    private const int Programs = 2_000;
    private const int Steps = 80;

    private static readonly RaceMemoryLocation[] Locations =
    [
        new(RaceMemoryLocationKind.StaticField, 0, "Model::A"),
        new(RaceMemoryLocationKind.StaticField, 0, "Model::B"),
        new(RaceMemoryLocationKind.StaticField, 0, "Model::C"),
        new(RaceMemoryLocationKind.StaticField, 0, "Model::D"),
        new(RaceMemoryLocationKind.StaticField, 0, "Model::E"),
        new(RaceMemoryLocationKind.StaticField, 0, "Model::F"),
    ];

    [Fact]
    public void StrandClocksReportTheFirstRaceAtTheSameAccessAsPerOperationClocks()
    {
        int raced = 0;
        for (int seed = 0; seed < Programs; seed++)
        {
            if (RunProgram(seed))
            {
                raced++;
            }
        }

        // Programs with and without a race are both well represented, so neither outcome is checked vacuously.
        Assert.InRange(raced, Programs / 10, Programs * 9 / 10);
    }

    private static bool RunProgram(int seed)
    {
        using SimulationScheduler scheduler = SchedulerTestHarness.NewScheduler(seed: seed);
        var random = new System.Random(seed);
        var tracker = new RaceTracker();
        var reference = new ReferenceTracker();
        var live = new List<SimulationOperation>();
        object[] locks = [new(), new()];
        object[] signals = [new(), new(), new()];
        var releases = new List<(Dictionary<long, long> Tracked, Dictionary<long, long> Reference)>();
        int nextOffset = 0;

        SimulationOperation Start(SimulationOperation? parent)
        {
            SimulationOperation operation = scheduler.Register("model", static () => { });
            tracker.RegisterOperation(operation, parent);
            reference.RegisterOperation(operation.Id.Value, parent?.Id.Value);
            live.Add(operation);
            return operation;
        }

        void Complete(SimulationOperation operation)
        {
            tracker.CompleteOperation(operation);
            live.Remove(operation);
        }

        // Without a lock, any location; under one lock, the half of the locations that lock guards.
        void Access(SimulationOperation operation, object[] held)
        {
            RaceAccessKind kind = random.Next(2) == 0 ? RaceAccessKind.Read : RaceAccessKind.Write;
            int guarded = Locations.Length / locks.Length;
            RaceMemoryLocation location = held.Length == 1
                ? Locations[(Array.IndexOf(locks, held[0]) * guarded) + random.Next(guarded)]
                : Locations[random.Next(Locations.Length)];
            var source = new RaceSourceLocation("model", ++nextOffset, null, -1);
            tracker.RecordAccess(operation, kind, location, source, []);
            reference.RecordAccess(operation.Id.Value, kind, location, source);
        }

        void Signal(SimulationOperation operation, object signal)
        {
            tracker.SignalSynchronization(operation, signal);
            reference.Signal(operation.Id.Value, signal);
        }

        void Wait(SimulationOperation operation, object signal)
        {
            tracker.WaitSynchronization(operation, signal);
            reference.Wait(operation.Id.Value, signal);
        }

        Start(parent: null);
        for (int step = 0;
            step < Steps && live.Count != 0 && tracker.FirstRace is null && reference.FirstRace is null;
            step++)
        {
            SimulationOperation operation = live[random.Next(live.Count)];
            switch (random.Next(100))
            {
                case < 5:
                    Start(operation);
                    break;
                case < 20:
                    // A continuation: the operation registers its successor and finishes.
                    Start(operation);
                    Complete(operation);
                    break;
                case < 30:
                    // A continuation which acquires the operation's last event, as an await of a task it completes.
                    object handoff = signals[random.Next(signals.Length)];
                    SimulationOperation successor = Start(operation);
                    Signal(operation, handoff);
                    Complete(operation);
                    Wait(successor, handoff);
                    break;
                case < 32:
                    // A root operation, as a timer callback, ordered after nothing.
                    Start(parent: null);
                    break;
                case < 38:
                    Access(operation, held: []);
                    break;
                case < 63:
                    // Atomic, as a controlled lock's section is, sometimes entered recursively, and sometimes preceded
                    // by an exit of a lock the operation does not hold, which records nothing.
                    object[] held = random.Next(3) == 0 ? locks : [locks[random.Next(locks.Length)]];
                    object[] entered = held.Length == 1 && random.Next(4) == 0 ? [held[0], held[0]] : held;
                    if (random.Next(8) == 0)
                    {
                        object unheld = locks[random.Next(locks.Length)];
                        tracker.ExitSynchronization(operation, unheld);
                        reference.Exit(operation.Id.Value, unheld);
                    }

                    foreach (object synchronization in entered)
                    {
                        tracker.EnterSynchronization(operation, synchronization);
                        reference.Enter(operation.Id.Value, synchronization);
                    }

                    for (int access = random.Next(1, 3); access > 0; access--)
                    {
                        Access(operation, held);
                    }

                    for (int exit = entered.Length - 1; exit >= 0; exit--)
                    {
                        tracker.ExitSynchronization(operation, entered[exit]);
                        reference.Exit(operation.Id.Value, entered[exit]);
                    }

                    break;
                case < 71:
                    Signal(operation, signals[random.Next(signals.Length)]);
                    break;
                case < 79:
                    Wait(operation, signals[random.Next(signals.Length)]);
                    break;
                case < 85:
                    releases.Add((tracker.CaptureRelease(operation), reference.CaptureRelease(operation.Id.Value)));
                    break;
                case < 91 when releases.Count != 0:
                    int index = random.Next(releases.Count);
                    tracker.ConsumeRelease(operation, releases[index].Tracked);
                    reference.ConsumeRelease(operation.Id.Value, releases[index].Reference);
                    releases.RemoveAt(index);
                    break;
                default:
                    Complete(operation);
                    break;
            }
        }

        Assert.Equal(reference.FirstRace is null, tracker.FirstRace is null);
        if (tracker.FirstRace is not { } race)
        {
            return false;
        }

        Assert.Equal(reference.FirstRace, (race.SecondAccess.OperationId.Value, race.SecondAccess.Source));
        Assert.Contains((race.FirstAccess.OperationId.Value, race.FirstAccess.Source), reference.RacingAccesses);
        return true;
    }

    // The detector before clocks were keyed by strand: a component per operation, and a full clock per access.
    private sealed class ReferenceTracker
    {
        private sealed record Access(long Operation, RaceSourceLocation Source, Dictionary<long, long> Clock, object[] Held);

        private sealed class Location
        {
            public Access? LastWrite;

            public SortedDictionary<long, Access> Readers { get; } = [];
        }

        private readonly Dictionary<long, Dictionary<long, long>> _clocks = [];
        private readonly Dictionary<long, Dictionary<object, int>> _held = [];
        private readonly Dictionary<object, Dictionary<long, long>> _lockClocks = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, Dictionary<long, long>> _signalClocks = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<RaceMemoryLocation, Location> _locations = [];

        public (long Operation, RaceSourceLocation Source)? FirstRace { get; private set; }

        // The earlier accesses the first race's second access races with.
        public List<(long Operation, RaceSourceLocation Source)> RacingAccesses { get; } = [];

        public void RegisterOperation(long operation, long? parent)
        {
            Dictionary<long, long> clock = [];
            if (parent is { } parentId)
            {
                Tick(parentId);
                Merge(clock, _clocks[parentId]);
            }

            clock[operation] = 1;
            _clocks.Add(operation, clock);
            _held.Add(operation, new Dictionary<object, int>(ReferenceEqualityComparer.Instance));
        }

        public void RecordAccess(long operation, RaceAccessKind kind, RaceMemoryLocation location, RaceSourceLocation source)
        {
            Tick(operation);
            var access = new Access(operation, source, new Dictionary<long, long>(_clocks[operation]), [.. _held[operation].Keys]);
            if (!_locations.TryGetValue(location, out Location? state))
            {
                state = new Location();
                _locations.Add(location, state);
            }

            if (FirstRace is null)
            {
                IEnumerable<Access> previous = kind == RaceAccessKind.Read ? [] : state.Readers.Values;
                foreach (Access earlier in previous.Prepend(state.LastWrite).OfType<Access>())
                {
                    if (Races(earlier, access))
                    {
                        RacingAccesses.Add((earlier.Operation, earlier.Source));
                    }
                }

                if (RacingAccesses.Count != 0)
                {
                    FirstRace = (operation, source);
                }
            }

            if (kind == RaceAccessKind.Read)
            {
                state.Readers[operation] = access;
                return;
            }

            state.Readers.Clear();
            state.LastWrite = access;
        }

        public void Enter(long operation, object synchronization)
        {
            if (_lockClocks.TryGetValue(synchronization, out Dictionary<long, long>? release))
            {
                Merge(_clocks[operation], release);
            }

            _held[operation][synchronization] = _held[operation].GetValueOrDefault(synchronization) + 1;
            Tick(operation);
        }

        public void Exit(long operation, object synchronization)
        {
            Dictionary<object, int> held = _held[operation];
            if (!held.TryGetValue(synchronization, out int recursion))
            {
                return;
            }

            Tick(operation);
            _lockClocks[synchronization] = new Dictionary<long, long>(_clocks[operation]);
            if (recursion == 1)
            {
                held.Remove(synchronization);
            }
            else
            {
                held[synchronization] = recursion - 1;
            }
        }

        public void Signal(long operation, object synchronization)
        {
            Tick(operation);
            if (_signalClocks.TryGetValue(synchronization, out Dictionary<long, long>? aggregate))
            {
                Merge(aggregate, _clocks[operation]);
            }
            else
            {
                _signalClocks[synchronization] = new Dictionary<long, long>(_clocks[operation]);
            }
        }

        public void Wait(long operation, object synchronization)
        {
            if (_signalClocks.TryGetValue(synchronization, out Dictionary<long, long>? release))
            {
                Merge(_clocks[operation], release);
            }

            Tick(operation);
        }

        public Dictionary<long, long> CaptureRelease(long operation)
        {
            Tick(operation);
            return new Dictionary<long, long>(_clocks[operation]);
        }

        public void ConsumeRelease(long operation, Dictionary<long, long> release)
        {
            Merge(_clocks[operation], release);
            Tick(operation);
        }

        private static bool Races(Access previous, Access current) =>
            previous.Operation != current.Operation &&
            !previous.Clock.All(entry => current.Clock.TryGetValue(entry.Key, out long epoch) && epoch >= entry.Value) &&
            !previous.Held.Intersect(current.Held, ReferenceEqualityComparer.Instance).Any();

        private void Tick(long operation)
        {
            Dictionary<long, long> clock = _clocks[operation];
            clock[operation] = clock.GetValueOrDefault(operation) + 1;
        }

        private static void Merge(Dictionary<long, long> target, Dictionary<long, long> source)
        {
            foreach ((long operation, long epoch) in source)
            {
                if (target.GetValueOrDefault(operation) < epoch)
                {
                    target[operation] = epoch;
                }
            }
        }
    }
}
