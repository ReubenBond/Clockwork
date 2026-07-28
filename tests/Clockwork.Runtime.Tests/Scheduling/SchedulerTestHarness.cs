using System.Collections.Concurrent;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Tests.Scheduling;

/// <summary>
/// Shared helpers for controlled-operation scheduler tests: a scheduler factory and a listener that
/// records the deterministic sequence of state transitions.
/// </summary>
internal static class SchedulerTestHarness
{
    public static SimulationScheduler NewScheduler(
        ISimulationOperationListener? listener = null,
        int seed = 1,
        string? description = null)
    {
        var runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, description);
        return new SimulationScheduler(runtime, listener);
    }
}

/// <summary>
/// A listener that records every state transition it observes, in order, as stable
/// <c>"opId:State"</c> strings for deterministic assertions.
/// </summary>
internal sealed class RecordingListener : ISimulationOperationListener
{
    private readonly ConcurrentQueue<(long Id, SimulationOperationState State)> _events = new();

    public IReadOnlyList<(long Id, SimulationOperationState State)> Events => _events.ToArray();

    public IReadOnlyList<string> Formatted => _events.Select(e => $"{e.Id}:{e.State}").ToArray();

    public void OnStateChanged(SimulationOperation operation, SimulationOperationState newState) =>
        _events.Enqueue((operation.Id.Value, newState));
}
