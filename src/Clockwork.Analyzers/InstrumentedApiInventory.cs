using System.Collections.Immutable;
using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Clockwork.Analyzers;

/// <summary>
/// The direct task and synchronization API surface whose shipped Clockwork rules require the
/// containing assembly to be instrumented. Entries are member-level because overload precision
/// remains the rewriter's responsibility; the analyzer's purpose is to make required instrumentation
/// visible at the source call site.
/// </summary>
public static class InstrumentedApiInventory
{
    private const string Wildcard = "*";

    private static readonly ImmutableHashSet<string> Members =
    [
        Api("System.Threading.Tasks.Task", "WhenAll"),
        Api("System.Threading.Tasks.Task", "WhenAny"),
        Api("System.Threading.Tasks.Task", "Wait"),
        Api("System.Threading.Tasks.Task", "WaitAll"),
        Api("System.Threading.Tasks.Task", "WaitAny"),
        Api("System.Threading.Tasks.Task", "ContinueWith"),
        Api("System.Threading.Tasks.Task", "Delay"),
        Api("System.Threading.Tasks.Task", "Run"),
        Api("System.Threading.Tasks.Task", "Yield"),
        Api("System.Threading.Tasks.Task`1", "Result"),
        Api("System.Threading.Tasks.Task`1", "ContinueWith"),
        Api("System.Threading.Tasks.TaskExtensions", "Unwrap"),
        Api("System.Threading.Tasks.TaskFactory", "StartNew"),
        Api("System.Threading.Tasks.TaskFactory`1", "StartNew"),
        Api("System.Threading.Thread", ".ctor"),
        Api("System.Threading.Thread", "Start"),
        Api("System.Threading.Thread", "Join"),
        Api("System.Threading.Thread", "Sleep"),
        Api("System.Threading.Thread", "SpinWait"),
        Api("System.Threading.Thread", "Yield"),
        Api("System.Threading.Thread", "Priority"),
        Api("System.Threading.Thread", "Interrupt"),
        Api("System.Threading.Thread", "SetApartmentState"),
        Api("System.Threading.Thread", "TrySetApartmentState"),
        Api("System.Threading.ThreadPool", "QueueUserWorkItem"),
        Api("System.Threading.ThreadPool", "UnsafeQueueUserWorkItem"),
        Api("System.Threading.ThreadPool", "UnsafeQueueNativeOverlapped"),
        Api("System.Threading.ThreadPool", "RegisterWaitForSingleObject"),
        Api("System.Threading.ThreadPool", "UnsafeRegisterWaitForSingleObject"),
        Api("System.Threading.Tasks.Parallel", "Invoke"),
        Api("System.Threading.Tasks.Parallel", "For"),
        Api("System.Threading.Tasks.Parallel", "ForEach"),
        Api("System.Threading.Monitor", "Enter"),
        Api("System.Threading.Monitor", "Exit"),
        Api("System.Threading.Monitor", "IsEntered"),
        Api("System.Threading.Monitor", "TryEnter"),
        Api("System.Threading.Monitor", "Wait"),
        Api("System.Threading.Monitor", "Pulse"),
        Api("System.Threading.Monitor", "PulseAll"),
        Api("System.Threading.Monitor", "LockContentionCount"),
        Api("System.Threading.Lock", Wildcard),
        Api("System.Threading.Lock+Scope", Wildcard),
        Api("System.Threading.SemaphoreSlim", ".ctor"),
        Api("System.Threading.SemaphoreSlim", "CurrentCount"),
        Api("System.Threading.SemaphoreSlim", "AvailableWaitHandle"),
        Api("System.Threading.SemaphoreSlim", "Wait"),
        Api("System.Threading.SemaphoreSlim", "WaitAsync"),
        Api("System.Threading.SemaphoreSlim", "Release"),
        Api("System.Threading.SemaphoreSlim", "Dispose"),
    ];

    /// <summary>Gets the metadata names of all framework types represented by the inventory.</summary>
    public static ImmutableArray<string> TypeMetadataNames { get; } =
        [.. Members.Select(static entry => entry.Substring(0, entry.IndexOf("::", StringComparison.Ordinal)))
            .Distinct(System.StringComparer.Ordinal)
            .OrderBy(static name => name, System.StringComparer.Ordinal)];

    /// <summary>Returns whether the named framework member requires shipped Clockwork instrumentation.</summary>
    public static bool Contains(string typeMetadataName, string memberName) =>
        Members.Contains(Api(typeMetadataName, memberName))
        || Members.Contains(Api(typeMetadataName, Wildcard));

    /// <summary>Returns whether the exact invocation shape is covered by shipped instrumentation.</summary>
    public static bool ContainsInvocation(string typeMetadataName, IMethodSymbol method)
    {
        if (!Contains(typeMetadataName, method.Name))
        {
            return false;
        }

        if (typeMetadataName == "System.Threading.Tasks.Task")
        {
            return method.Name switch
            {
                "Wait" => method.Parameters.Length == 0,
                "WaitAll" or "WaitAny" =>
                    method.Parameters.Length == 1
                    && method.Parameters[0].Type is IArrayTypeSymbol array
                    && array.ElementType.ToDisplayString() == "System.Threading.Tasks.Task",
                "Delay" =>
                    method.Parameters.Length == 1
                    && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32,
                _ => true,
            };
        }

        if (typeMetadataName is "System.Threading.Tasks.TaskFactory" or "System.Threading.Tasks.TaskFactory`1")
        {
            return method.Name != "StartNew"
                || (method.Parameters.Length <= 3
                    && !method.Parameters.Any(parameter =>
                        parameter.Type.ToDisplayString() == "System.Threading.Tasks.TaskScheduler"));
        }

        return true;
    }

    /// <summary>Returns whether all operations over the named framework type are instrumented.</summary>
    public static bool ContainsType(string typeMetadataName) =>
        Members.Contains(Api(typeMetadataName, Wildcard));

    private static string Api(string typeMetadataName, string memberName) =>
        typeMetadataName + "::" + memberName;
}
