using System.ComponentModel;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>Controlled rewrite targets for <see cref="DateTimeOffset"/> clock properties.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ControlledDateTimeOffset
{
    /// <summary>Controlled replacement for <see cref="DateTimeOffset.Now"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTimeOffset GetNow()
    {
        var (_, environment, node) =
            SimulationRuntimeDispatch.RequireEnvironment("System.DateTimeOffset.Now");
        return ToLocal(environment, node);
    }

    /// <summary>Controlled replacement for <see cref="DateTimeOffset.UtcNow"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTimeOffset GetUtcNow()
    {
        var (_, environment, node) =
            SimulationRuntimeDispatch.RequireEnvironment("System.DateTimeOffset.UtcNow");
        return environment.GetUtcNow(node).ToUniversalTime();
    }

    private static DateTimeOffset ToLocal(
        ISimulationRuntimeEnvironment environment,
        SimulationNodeIdentity? node)
    {
        var utc = environment.GetUtcNow(node).ToUniversalTime();
        return TimeZoneInfo.ConvertTime(utc, environment.GetLocalTimeZone(node));
    }
}
