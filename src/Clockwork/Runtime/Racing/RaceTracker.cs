using System.Runtime.CompilerServices;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Racing;

/// <summary>Scheduler-owned vector-clock and lockset race detector.</summary>
/// <remarks>
/// <para>
/// Happens-before is the transitive closure of program order within an operation, the edge from an operation to each
/// operation it registers, and the release-to-acquire edges of controlled synchronization: a lock's exit to its next
/// enter, every signal of an object to each later wait on it, and a resource signal to the wait it resolves. Two
/// accesses to one location race when at least one is a write, neither happens before the other, and they hold no
/// controlled lock in common.
/// </para>
/// <para>
/// Clock components are keyed by strand, not by operation. A strand is a sequence of operations in which every event of
/// each operation happens before every event of the next: an operation joins a finished operation's strand at its
/// first event when its clock already covers that operation's last event, as an await continuation's clock covers the
/// fork which registered it. The events of a strand are therefore totally ordered by happens-before, so a clock is
/// exact with one component per strand, an access is retained as its strand and epoch, and an earlier access happens
/// before a later one exactly when the later access's clock covers the earlier one's epoch. A strand's later read
/// supersedes its earlier read of the same location, as an operation's did: the earlier read happens before the later
/// one, so a write which the earlier read does not happen before, the later read does not happen before either, and
/// the later read holds no lock in common with such a write because every lock recorded here is exclusive (see
/// <see cref="EnterSynchronization"/>). A write racing several reads is reported against the read of the lowest strand.
/// </para>
/// <para>
/// An operation's state is dropped when it terminates. A finished strand stays live only while a location retains one
/// of its accesses; afterwards no retained access can be compared against it and no operation can join it, so its clock
/// entries are dropped wherever clocks are copied or merged. Lock and signal clocks live as long as their
/// synchronization object. Every clock is therefore bounded by the number of live strands, which is the number of
/// running operations plus the finished strands whose accesses locations retain as their last write or reads, however
/// long a chain of continuations runs.
/// </para>
/// </remarks>
internal sealed class RaceTracker
{
    private sealed class WeakIdentity
    {
        public required long Value { get; init; }
    }

    private sealed class Strand
    {
        // Whether a non-terminated operation owns the strand; a strand with no owner can be joined.
        public bool IsOwned = true;

        // The epoch of the strand's last event, once its owner has terminated.
        public long FinalEpoch;

        // The number of accesses locations retain as their last write or a read.
        public int RetainedAccesses;
    }

    private sealed class OperationState(Dictionary<long, long> clock)
    {
        public Dictionary<long, long> Clock { get; } = clock;

        public Dictionary<long, int> HeldSynchronization { get; } = [];

        // The operation's strand, or zero before its first event.
        public long Strand;
    }

    private sealed class LockState
    {
        public required long Id { get; init; }

        public Dictionary<long, long>? ReleaseClock;
    }

    private sealed class SignalState
    {
        public Dictionary<long, long>? Clock;
    }

    private sealed class AccessState
    {
        public TrackedAccess? LastWrite;

        // Each strand's latest read since the last write, by strand.
        public SortedDictionary<long, TrackedAccess> Readers { get; } = [];
    }

    private sealed record TrackedAccess(
        SimulationOperationId OperationId,
        long Strand,
        long Epoch,
        RaceAccessKind Kind,
        RaceMemoryLocation Location,
        RaceSourceLocation Source,
        long[] HeldSynchronization);

    private readonly ConditionalWeakTable<object, WeakIdentity> _objectIdentities = new();
    private readonly ConditionalWeakTable<object, LockState> _locks = new();
    private readonly ConditionalWeakTable<object, SignalState> _signals = new();
    private readonly Dictionary<long, OperationState> _operations = [];
    private readonly Dictionary<long, Strand> _strands = [];
    private readonly Dictionary<RaceMemoryLocation, AccessState> _locations = [];
    private long _nextObjectIdentity;
    private long _nextSynchronizationIdentity;
    private long _nextStrand;
    private int _largestClock;

    public RaceReport? FirstRace { get; private set; }

    /// <summary>Gets the size of the tracker's retained state.</summary>
    public RaceTrackerStatistics Statistics => new(_operations.Count, _strands.Count, _largestClock);

    public void RegisterOperation(SimulationOperation operation, SimulationOperation? parent)
    {
        Dictionary<long, long> clock = [];
        if (parent is not null)
        {
            OperationState parentState = StateOf(parent);
            Tick(parentState);
            clock = Copy(parentState.Clock);
        }

        _operations.Add(operation.Id.Value, new OperationState(clock));
    }

    /// <summary>
    /// Drops a terminated operation's state, which ends its strand unless a location retains one of the strand's
    /// accesses; until then an operation whose clock covers the strand's last event may join it.
    /// </summary>
    public void CompleteOperation(SimulationOperation operation)
    {
        if (!_operations.Remove(operation.Id.Value, out OperationState? state))
        {
            throw new SimulationSchedulerException($"Race state for {operation.Id} was not registered.");
        }

        if (state.Strand == 0)
        {
            return;
        }

        Strand strand = _strands[state.Strand];
        strand.IsOwned = false;
        strand.FinalEpoch = state.Clock[state.Strand];
        EndIfUnused(state.Strand, strand);
    }

    public RaceMemoryLocation? ResolveLocation(
        RaceMemoryLocationKind? kind,
        object? target,
        string member,
        long? elementIndex)
    {
        if (kind is null)
        {
            return null;
        }

        if (kind == RaceMemoryLocationKind.StaticField && target is null)
        {
            return new RaceMemoryLocation(kind.Value, 0, member);
        }

        if (target is null)
        {
            return null;
        }

        long objectId = _objectIdentities.GetValue(
            target,
            _ => new WeakIdentity { Value = ++_nextObjectIdentity }).Value;
        string locationMember = kind == RaceMemoryLocationKind.Collection &&
            member.IndexOf("::", StringComparison.Ordinal) is int separator &&
            separator >= 0
                ? member[..separator]
                : member;
        return new RaceMemoryLocation(kind.Value, objectId, locationMember, elementIndex);
    }

    public void RecordAccess(
        SimulationOperation operation,
        RaceAccessKind kind,
        RaceMemoryLocation location,
        RaceSourceLocation source,
        IReadOnlyList<RaceSchedulingPoint> trace)
    {
        if (kind is not RaceAccessKind.Read and not RaceAccessKind.Write)
        {
            return;
        }

        OperationState operationState = StateOf(operation);
        Tick(operationState);
        var access = new TrackedAccess(
            operation.Id,
            operationState.Strand,
            operationState.Clock[operationState.Strand],
            kind,
            location,
            source,
            CaptureHeldSynchronization(operationState));
        if (!_locations.TryGetValue(location, out AccessState? locationState))
        {
            locationState = new AccessState();
            _locations.Add(location, locationState);
        }

        DetectConflict(locationState.LastWrite, access, operationState.Clock, trace);
        if (kind == RaceAccessKind.Read)
        {
            if (locationState.Readers.Remove(access.Strand, out TrackedAccess? superseded))
            {
                Release(superseded);
            }

            locationState.Readers.Add(access.Strand, access);
            Retain(access);
            return;
        }

        foreach (TrackedAccess reader in locationState.Readers.Values)
        {
            DetectConflict(reader, access, operationState.Clock, trace);
            Release(reader);
        }

        locationState.Readers.Clear();
        if (locationState.LastWrite is { } previousWrite)
        {
            Release(previousWrite);
        }

        locationState.LastWrite = access;
        Retain(access);
    }

    public bool HasHeldSynchronization(SimulationOperation operation) =>
        StateOf(operation).HeldSynchronization.Count != 0;

    /// <summary>
    /// Records acquisition of an exclusive lock: its holders are ordered by its exit-to-enter edges, so an access under
    /// the lock happens before every later access under it. Shared holds, such as a reader lock, publish and consume
    /// edges through <see cref="SignalSynchronization"/> and <see cref="WaitSynchronization"/> instead.
    /// </summary>
    public void EnterSynchronization(SimulationOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        LockState lockState = LockOf(synchronization);
        if (lockState.ReleaseClock is { } releaseClock)
        {
            Merge(state.Clock, releaseClock);
        }

        state.HeldSynchronization.TryGetValue(lockState.Id, out int recursion);
        state.HeldSynchronization[lockState.Id] = recursion + 1;
        Tick(state);
    }

    public void ExitSynchronization(SimulationOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        LockState lockState = LockOf(synchronization);
        if (!state.HeldSynchronization.TryGetValue(lockState.Id, out int recursion))
        {
            return;
        }

        Tick(state);
        lockState.ReleaseClock = Copy(state.Clock);
        if (recursion == 1)
        {
            state.HeldSynchronization.Remove(lockState.Id);
        }
        else
        {
            state.HeldSynchronization[lockState.Id] = recursion - 1;
        }
    }

    public void SignalSynchronization(SimulationOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        Tick(state);
        SignalState signal = _signals.GetValue(synchronization, static _ => new SignalState());
        if (signal.Clock is { } aggregate)
        {
            Merge(aggregate, state.Clock);
        }
        else
        {
            signal.Clock = Copy(state.Clock);
        }
    }

    public void WaitSynchronization(SimulationOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        if (_signals.TryGetValue(synchronization, out SignalState? signal) && signal.Clock is { } releaseClock)
        {
            Merge(state.Clock, releaseClock);
        }

        Tick(state);
    }

    public Dictionary<long, long> CaptureRelease(SimulationOperation operation)
    {
        OperationState state = StateOf(operation);
        Tick(state);
        return Copy(state.Clock);
    }

    public void ConsumeRelease(SimulationOperation operation, Dictionary<long, long> releaseClock)
    {
        OperationState state = StateOf(operation);
        Merge(state.Clock, releaseClock);
        Tick(state);
    }

    private void DetectConflict(
        TrackedAccess? previous,
        TrackedAccess current,
        Dictionary<long, long> currentClock,
        IReadOnlyList<RaceSchedulingPoint> trace)
    {
        if (FirstRace is not null ||
            previous is null ||
            HappensBefore(previous, currentClock) ||
            HasCommonSynchronization(previous.HeldSynchronization, current.HeldSynchronization))
        {
            return;
        }

        FirstRace = new RaceReport
        {
            FirstAccess = CreatePublicAccess(previous),
            SecondAccess = CreatePublicAccess(current),
            ScheduleTrace = [.. trace],
        };
    }

    // Exact because a strand's events are totally ordered: the clock covers the access's epoch only through an edge
    // from that access or a later event of its strand.
    private static bool HappensBefore(TrackedAccess previous, Dictionary<long, long> currentClock) =>
        currentClock.TryGetValue(previous.Strand, out long epoch) && epoch >= previous.Epoch;

    private static long[] CaptureHeldSynchronization(OperationState state)
    {
        if (state.HeldSynchronization.Count == 0)
        {
            return [];
        }

        var held = new long[state.HeldSynchronization.Count];
        state.HeldSynchronization.Keys.CopyTo(held, 0);
        Array.Sort(held);
        return held;
    }

    private static RaceAccessRecord CreatePublicAccess(TrackedAccess access) =>
        new(
            access.OperationId,
            access.Kind,
            access.Location,
            access.Source,
            [.. access.HeldSynchronization.Select(static id => $"sync#{id}")]);

    private OperationState StateOf(SimulationOperation operation) =>
        _operations.TryGetValue(operation.Id.Value, out OperationState? state)
            ? state
            : throw new SimulationSchedulerException($"Race state for {operation.Id} was not registered.");

    private LockState LockOf(object synchronization) =>
        _locks.GetValue(synchronization, _ => new LockState { Id = ++_nextSynchronizationIdentity });

    private static bool HasCommonSynchronization(
        long[] first,
        long[] second)
    {
        int left = 0;
        int right = 0;
        while (left < first.Length && right < second.Length)
        {
            if (first[left] == second[right])
            {
                return true;
            }

            if (first[left] < second[right])
            {
                left++;
            }
            else
            {
                right++;
            }
        }

        return false;
    }

    /// <summary>Records the operation's next event on its strand, joining or starting one at its first event.</summary>
    private void Tick(OperationState state)
    {
        if (state.Strand == 0)
        {
            state.Strand = JoinOrStartStrand(state.Clock);
        }

        state.Clock.TryGetValue(state.Strand, out long epoch);
        state.Clock[state.Strand] = epoch + 1;
        _largestClock = Math.Max(_largestClock, state.Clock.Count);
    }

    // Joins the lowest finished strand whose last event the clock covers, which every event of the joining operation
    // follows; otherwise starts a new strand.
    private long JoinOrStartStrand(Dictionary<long, long> clock)
    {
        long joined = 0;
        foreach ((long id, long epoch) in clock)
        {
            if ((joined == 0 || id < joined) &&
                _strands.TryGetValue(id, out Strand? candidate) &&
                !candidate.IsOwned &&
                candidate.FinalEpoch == epoch)
            {
                joined = id;
            }
        }

        if (joined != 0)
        {
            _strands[joined].IsOwned = true;
            return joined;
        }

        long started = ++_nextStrand;
        _strands.Add(started, new Strand());
        return started;
    }

    private void Retain(TrackedAccess access) => _strands[access.Strand].RetainedAccesses++;

    private void Release(TrackedAccess access)
    {
        Strand strand = _strands[access.Strand];
        strand.RetainedAccesses--;
        EndIfUnused(access.Strand, strand);
    }

    private void EndIfUnused(long id, Strand strand)
    {
        if (!strand.IsOwned && strand.RetainedAccesses == 0)
        {
            _strands.Remove(id);
        }
    }

    private Dictionary<long, long> Copy(Dictionary<long, long> source)
    {
        var copy = new Dictionary<long, long>(Math.Min(source.Count, _strands.Count));
        foreach ((long strand, long epoch) in source)
        {
            if (_strands.ContainsKey(strand))
            {
                copy.Add(strand, epoch);
            }
        }

        _largestClock = Math.Max(_largestClock, copy.Count);
        return copy;
    }

    private void Merge(Dictionary<long, long> target, Dictionary<long, long> source)
    {
        foreach ((long strand, long epoch) in source)
        {
            if (_strands.ContainsKey(strand) &&
                (!target.TryGetValue(strand, out long current) || current < epoch))
            {
                target[strand] = epoch;
            }
        }

        // A clock with more entries than there are live strands holds an ended strand's entry.
        if (target.Count > _strands.Count)
        {
            foreach (long strand in target.Keys)
            {
                if (!_strands.ContainsKey(strand))
                {
                    target.Remove(strand);
                }
            }
        }

        _largestClock = Math.Max(_largestClock, target.Count);
    }
}

/// <summary>The size of a <see cref="RaceTracker"/>'s retained state.</summary>
/// <param name="Operations">The operations whose state is retained: those which have not terminated.</param>
/// <param name="Strands">The live strands: those owned by an operation or retained by a location's access.</param>
/// <param name="LargestClock">The most components any clock has held.</param>
internal readonly record struct RaceTrackerStatistics(int Operations, int Strands, int LargestClock);
