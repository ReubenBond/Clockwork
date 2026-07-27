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
    public static ControlledOperationScheduler NewScheduler(
        IControlledOperationListener? listener = null,
        int seed = 1,
        string? description = null)
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, description);
        return new ControlledOperationScheduler(token, runtime, listener);
    }
}

/// <summary>
/// A listener that records every state transition it observes, in order, as stable
/// <c>"opId:State"</c> strings for deterministic assertions.
/// </summary>
internal sealed class RecordingListener : IControlledOperationListener
{
    private readonly ConcurrentQueue<(long Id, ControlledOperationState State)> _events = new();

    public IReadOnlyList<(long Id, ControlledOperationState State)> Events => _events.ToArray();

    public IReadOnlyList<string> Formatted => _events.Select(e => $"{e.Id}:{e.State}").ToArray();

    public void OnStateChanged(ControlledOperation operation, ControlledOperationState newState) =>
        _events.Enqueue((operation.Id.Value, newState));
}
