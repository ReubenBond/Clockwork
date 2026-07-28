using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Racing;

/// <summary>Scheduler-owned vector-clock and lockset race detector.</summary>
internal sealed class RaceTracker
{
    private sealed class WeakIdentity
    {
        public required long Value { get; init; }
    }

    private sealed class OperationState
    {
        public Dictionary<long, long> Clock { get; } = [];

        public Dictionary<long, int> HeldSynchronization { get; } = [];
    }

    private sealed class AccessState
    {
        public TrackedAccess? LastWrite;

        public SortedDictionary<long, TrackedAccess> Readers { get; } = [];
    }

    private sealed record TrackedAccess(
        RaceAccessRecord Public,
        Dictionary<long, long> Clock,
        ImmutableArray<long> HeldSynchronization);

    private readonly ConditionalWeakTable<object, WeakIdentity> _objectIdentities = new();
    private readonly ConditionalWeakTable<object, WeakIdentity> _synchronizationIdentities = new();
    private readonly Dictionary<long, OperationState> _operations = [];
    private readonly Dictionary<RaceMemoryLocation, AccessState> _locations = [];
    private readonly Dictionary<long, Dictionary<long, long>> _synchronizationClocks = [];
    private long _nextObjectIdentity;
    private long _nextSynchronizationIdentity;

    public RaceReport? FirstRace { get; private set; }

    public void RegisterOperation(ControlledOperation operation, ControlledOperation? parent)
    {
        var state = new OperationState();
        if (parent is not null && _operations.TryGetValue(parent.Id.Value, out OperationState? parentState))
        {
            Tick(parent.Id.Value, parentState.Clock);
            Merge(state.Clock, parentState.Clock);
        }

        state.Clock[operation.Id.Value] = 1;
        _operations.Add(operation.Id.Value, state);
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

        if (kind == RaceMemoryLocationKind.StaticField)
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
        ControlledOperation operation,
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
        Tick(operation.Id.Value, operationState.Clock);
        var held = operationState.HeldSynchronization.Keys.Order().ToImmutableArray();
        var publicAccess = new RaceAccessRecord(
            operation.Id,
            kind,
            location,
            source,
            [.. held.Select(static id => $"sync#{id}")]);
        var access = new TrackedAccess(publicAccess, new Dictionary<long, long>(operationState.Clock), held);
        if (!_locations.TryGetValue(location, out AccessState? locationState))
        {
            locationState = new AccessState();
            _locations.Add(location, locationState);
        }

        if (kind == RaceAccessKind.Read)
        {
            DetectConflict(locationState.LastWrite, access, trace);
            locationState.Readers[operation.Id.Value] = access;
            return;
        }

        DetectConflict(locationState.LastWrite, access, trace);
        foreach (TrackedAccess reader in locationState.Readers.Values)
        {
            DetectConflict(reader, access, trace);
            if (FirstRace is not null)
            {
                break;
            }
        }

        locationState.Readers.Clear();
        locationState.LastWrite = access;
    }

    public bool HasHeldSynchronization(ControlledOperation operation) =>
        StateOf(operation).HeldSynchronization.Count != 0;

    public void EnterSynchronization(ControlledOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        long id = SynchronizationId(synchronization);
        if (_synchronizationClocks.TryGetValue(id, out Dictionary<long, long>? releaseClock))
        {
            Merge(state.Clock, releaseClock);
        }

        state.HeldSynchronization.TryGetValue(id, out int recursion);
        state.HeldSynchronization[id] = recursion + 1;
        Tick(operation.Id.Value, state.Clock);
    }

    public void ExitSynchronization(ControlledOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        long id = SynchronizationId(synchronization);
        if (!state.HeldSynchronization.TryGetValue(id, out int recursion))
        {
            return;
        }

        Tick(operation.Id.Value, state.Clock);
        _synchronizationClocks[id] = new Dictionary<long, long>(state.Clock);
        if (recursion == 1)
        {
            state.HeldSynchronization.Remove(id);
        }
        else
        {
            state.HeldSynchronization[id] = recursion - 1;
        }
    }

    public void SignalSynchronization(ControlledOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        Tick(operation.Id.Value, state.Clock);
        _synchronizationClocks[SynchronizationId(synchronization)] = new Dictionary<long, long>(state.Clock);
    }

    public void WaitSynchronization(ControlledOperation operation, object synchronization)
    {
        OperationState state = StateOf(operation);
        if (_synchronizationClocks.TryGetValue(
            SynchronizationId(synchronization),
            out Dictionary<long, long>? releaseClock))
        {
            Merge(state.Clock, releaseClock);
        }

        Tick(operation.Id.Value, state.Clock);
    }

    private void DetectConflict(
        TrackedAccess? previous,
        TrackedAccess current,
        IReadOnlyList<RaceSchedulingPoint> trace)
    {
        if (FirstRace is not null ||
            previous is null ||
            previous.Public.OperationId == current.Public.OperationId ||
            HappensBefore(previous.Clock, current.Clock) ||
            HasCommonSynchronization(previous.HeldSynchronization, current.HeldSynchronization))
        {
            return;
        }

        FirstRace = new RaceReport
        {
            FirstAccess = previous.Public,
            SecondAccess = current.Public,
            ScheduleTrace = [.. trace],
        };
    }

    private OperationState StateOf(ControlledOperation operation) =>
        _operations.TryGetValue(operation.Id.Value, out OperationState? state)
            ? state
            : throw new ControlledOperationException($"Race state for {operation.Id} was not registered.");

    private long SynchronizationId(object synchronization) =>
        _synchronizationIdentities.GetValue(
            synchronization,
            _ => new WeakIdentity { Value = ++_nextSynchronizationIdentity }).Value;

    private static bool HappensBefore(
        Dictionary<long, long> previous,
        Dictionary<long, long> current)
    {
        foreach ((long operation, long epoch) in previous)
        {
            if (!current.TryGetValue(operation, out long currentEpoch) || currentEpoch < epoch)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCommonSynchronization(
        ImmutableArray<long> first,
        ImmutableArray<long> second)
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

    private static void Tick(long operation, Dictionary<long, long> clock)
    {
        clock.TryGetValue(operation, out long current);
        clock[operation] = current + 1;
    }

    private static void Merge(Dictionary<long, long> target, IReadOnlyDictionary<long, long> source)
    {
        foreach ((long operation, long epoch) in source)
        {
            if (!target.TryGetValue(operation, out long current) || current < epoch)
            {
                target[operation] = epoch;
            }
        }
    }
}
