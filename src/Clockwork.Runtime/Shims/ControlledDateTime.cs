using System.ComponentModel;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>Controlled rewrite targets for <see cref="DateTime"/> clock properties.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ControlledDateTime
{
    /// <summary>Controlled replacement for <see cref="DateTime.Now"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetNow()
    {
        var (_, environment, node) = SimulationRuntimeDispatch.RequireEnvironment("System.DateTime.Now");
        var local = ToLocal(environment, node);
        return DateTime.SpecifyKind(local.DateTime, DateTimeKind.Local);
    }

    /// <summary>Controlled replacement for <see cref="DateTime.UtcNow"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetUtcNow()
    {
        var (_, environment, node) = SimulationRuntimeDispatch.RequireEnvironment("System.DateTime.UtcNow");
        return environment.GetUtcNow(node).UtcDateTime;
    }

    /// <summary>Controlled replacement for <see cref="DateTime.Today"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetToday()
    {
        var (_, environment, node) = SimulationRuntimeDispatch.RequireEnvironment("System.DateTime.Today");
        var local = ToLocal(environment, node);
        return DateTime.SpecifyKind(local.Date, DateTimeKind.Local);
    }

    private static DateTimeOffset ToLocal(
        ISimulationRuntimeEnvironment environment,
        SimulationNodeIdentity? node)
    {
        var utc = environment.GetUtcNow(node).ToUniversalTime();
        return TimeZoneInfo.ConvertTime(utc, environment.GetLocalTimeZone(node));
    }
}
