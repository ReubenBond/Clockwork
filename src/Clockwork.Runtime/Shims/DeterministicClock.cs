using System.ComponentModel;
using System.Diagnostics;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// The deterministic replacements for the static clock and timestamp BCL APIs. Instrumented code has
/// its direct calls to <see cref="DateTime.Now"/>, <see cref="DateTime.UtcNow"/>,
/// <see cref="DateTime.Today"/>, <see cref="DateTimeOffset.Now"/>, <see cref="DateTimeOffset.UtcNow"/>,
/// <see cref="Stopwatch.GetTimestamp"/>, <see cref="Stopwatch.GetElapsedTime(long)"/>,
/// <see cref="Environment.TickCount"/>, and <see cref="Environment.TickCount64"/> redirected here.
/// </para>
/// <para>
/// Each method follows the same contract via <see cref="SimulationRuntimeDispatch"/>: outside a
/// simulation it calls the real BCL API (production pass-through); inside a simulation with a
/// registered environment it returns virtual time; inside a simulation with no registered environment
/// it throws <see cref="SimulationServiceMissingException"/>.
/// </para>
/// <para>
/// This type is public because instrumented assemblies call into it, and its methods are hidden from
/// IntelliSense with <see cref="EditorBrowsableAttribute"/> because they are an instrumentation
/// implementation detail, not an API humans call directly.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeterministicClock
{
    /// <summary>Deterministic replacement for <see cref="DateTime.Now"/>.</summary>
    /// <returns>The virtual local time when simulating; otherwise <see cref="DateTime.Now"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetNow()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.DateTime.Now", out var env, out var node))
        {
            return DateTime.Now;
        }

        var local = ToLocal(env, node);
        return DateTime.SpecifyKind(local.DateTime, DateTimeKind.Local);
    }

    /// <summary>Deterministic replacement for <see cref="DateTime.UtcNow"/>.</summary>
    /// <returns>The virtual UTC time when simulating; otherwise <see cref="DateTime.UtcNow"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetUtcNow()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.DateTime.UtcNow", out var env, out var node))
        {
            return DateTime.UtcNow;
        }

        return env.GetUtcNow(node).UtcDateTime;
    }

    /// <summary>Deterministic replacement for <see cref="DateTime.Today"/>.</summary>
    /// <returns>The virtual local date when simulating; otherwise <see cref="DateTime.Today"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTime GetToday()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.DateTime.Today", out var env, out var node))
        {
            return DateTime.Today;
        }

        var local = ToLocal(env, node);
        return DateTime.SpecifyKind(local.Date, DateTimeKind.Local);
    }

    /// <summary>Deterministic replacement for <see cref="DateTimeOffset.Now"/>.</summary>
    /// <returns>The virtual local offset time when simulating; otherwise <see cref="DateTimeOffset.Now"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTimeOffset GetOffsetNow()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.DateTimeOffset.Now", out var env, out var node))
        {
            return DateTimeOffset.Now;
        }

        return ToLocal(env, node);
    }

    /// <summary>Deterministic replacement for <see cref="DateTimeOffset.UtcNow"/>.</summary>
    /// <returns>The virtual UTC offset time when simulating; otherwise <see cref="DateTimeOffset.UtcNow"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static DateTimeOffset GetOffsetUtcNow()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.DateTimeOffset.UtcNow", out var env, out var node))
        {
            return DateTimeOffset.UtcNow;
        }

        return env.GetUtcNow(node).ToUniversalTime();
    }

    /// <summary>Deterministic replacement for <see cref="Stopwatch.GetTimestamp"/>.</summary>
    /// <returns>The virtual timestamp when simulating; otherwise <see cref="Stopwatch.GetTimestamp"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static long GetTimestamp()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Diagnostics.Stopwatch.GetTimestamp", out var env, out var node))
        {
            return Stopwatch.GetTimestamp();
        }

        return checked((long)((decimal)env.GetTimestamp(node) * Stopwatch.Frequency / TimeSpan.TicksPerSecond));
    }

    /// <summary>Deterministic replacement for <see cref="Stopwatch.GetElapsedTime(long)"/>.</summary>
    /// <param name="startingTimestamp">The starting timestamp, typically from <see cref="GetTimestamp"/>.</param>
    /// <returns>The virtual elapsed time when simulating; otherwise <see cref="Stopwatch.GetElapsedTime(long)"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TimeSpan GetElapsedTime(long startingTimestamp)
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Diagnostics.Stopwatch.GetElapsedTime", out var env, out var node))
        {
            return Stopwatch.GetElapsedTime(startingTimestamp);
        }

        long endingTimestamp = checked(
            (long)((decimal)env.GetTimestamp(node) * Stopwatch.Frequency / TimeSpan.TicksPerSecond));
        return Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
    }

    /// <summary>Deterministic replacement for <see cref="Environment.TickCount"/>.</summary>
    /// <returns>The virtual tick count when simulating; otherwise <see cref="Environment.TickCount"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int GetTickCount()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Environment.TickCount", out var env, out var node))
        {
            return Environment.TickCount;
        }

        // Match Environment.TickCount's documented 32-bit wrap: it is the low 32 bits of TickCount64.
        return unchecked((int)env.GetTickCount64(node));
    }

    /// <summary>Deterministic replacement for <see cref="Environment.TickCount64"/>.</summary>
    /// <returns>The virtual 64-bit tick count when simulating; otherwise <see cref="Environment.TickCount64"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static long GetTickCount64()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Environment.TickCount64", out var env, out var node))
        {
            return Environment.TickCount64;
        }

        return env.GetTickCount64(node);
    }

    private static DateTimeOffset ToLocal(ISimulationRuntimeEnvironment environment, SimulationNodeIdentity? node)
    {
        var utc = environment.GetUtcNow(node).ToUniversalTime();
        var timeZone = environment.GetLocalTimeZone(node);
        return TimeZoneInfo.ConvertTime(utc, timeZone);
    }
}
