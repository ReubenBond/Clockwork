using System.ComponentModel;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Racing;

/// <summary>
/// Runtime targets for race-exploration IL instrumentation. Calls are no-ops unless they execute
/// inside a <see cref="ControlledOperationScheduler"/> operation.
/// </summary>
/// <remarks>These methods are intended for generated IL rather than direct application use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RaceInstrumentation
{
    [ThreadStatic]
    private static bool t_isProcessingPoint;

    /// <summary>Records and schedules an instance-field read.</summary>
    public static void ReadInstance(
        object? target,
        string member,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.Read, member, method, ilOffset, sourceFile, sourceLine);

    /// <summary>Records and schedules an instance-field write.</summary>
    public static void WriteInstance(
        object? target,
        string member,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.Write, member, method, ilOffset, sourceFile, sourceLine);

    /// <summary>Records and schedules a static-field read.</summary>
    public static void ReadStatic(
        string member,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.Read, member, method, ilOffset, sourceFile, sourceLine);

    /// <summary>Records and schedules a static-field write.</summary>
    public static void WriteStatic(
        string member,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.Write, member, method, ilOffset, sourceFile, sourceLine);

    /// <summary>Records and schedules an array-element read.</summary>
    public static void ReadArray(
        Array? target,
        long index,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(
            RaceAccessKind.Read,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"array[{index}]"),
            method,
            ilOffset,
            sourceFile,
            sourceLine);

    /// <summary>Records and schedules an array-element write.</summary>
    public static void WriteArray(
        Array? target,
        long index,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(
            RaceAccessKind.Write,
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"array[{index}]"),
            method,
            ilOffset,
            sourceFile,
            sourceLine);

    /// <summary>Schedules a memory access whose stable logical location cannot be recovered from IL.</summary>
    public static void InterleaveUntrackedMemory(
        string description,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.UntrackedMemory, description, method, ilOffset, sourceFile, sourceLine);

    /// <summary>Schedules a conditional control-flow branch.</summary>
    public static void InterleaveControlFlow(
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine) =>
        Interleave(RaceAccessKind.ControlFlow, "conditional branch", method, ilOffset, sourceFile, sourceLine);

    private static void Interleave(
        RaceAccessKind kind,
        string location,
        string method,
        int ilOffset,
        string? sourceFile,
        int sourceLine)
    {
        if (t_isProcessingPoint ||
            !ControlledOperationScheduler.TryGetExecutingOperation(out ControlledOperationScheduler? scheduler, out _))
        {
            return;
        }

        t_isProcessingPoint = true;
        try
        {
            scheduler.ReachRaceSchedulingPoint(
                kind,
                location,
                new RaceSourceLocation(method, ilOffset, sourceFile, sourceLine));
        }
        finally
        {
            t_isProcessingPoint = false;
        }
    }
}
