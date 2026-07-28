using System.ComponentModel;

namespace Clockwork.Runtime.Shims;

/// <summary>Controlled rewrite targets for <see cref="Environment"/> APIs.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ControlledEnvironment
{
    /// <summary>Controlled replacement for <see cref="Environment.CurrentManagedThreadId"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int GetCurrentManagedThreadId()
    {
        _ = SimulationRuntimeDispatch.RequireEnvironment("System.Environment.CurrentManagedThreadId");
        return Scheduling.SimulationScheduler.SimulationLogicalThreadOwnerId;
    }

    /// <summary>Controlled replacement for <see cref="Environment.TickCount"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int GetTickCount()
    {
        var (_, environment, node) =
            SimulationRuntimeDispatch.RequireEnvironment("System.Environment.TickCount");
        return unchecked((int)environment.GetTickCount64(node));
    }

    /// <summary>Controlled replacement for <see cref="Environment.TickCount64"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static long GetTickCount64()
    {
        var (_, environment, node) =
            SimulationRuntimeDispatch.RequireEnvironment("System.Environment.TickCount64");
        return environment.GetTickCount64(node);
    }
}
