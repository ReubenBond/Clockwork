namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// Why a public or protected member of a type the built-in rules target stays a BCL call. Each value names one
/// behavior that the rendered rule inventory describes, and <see cref="UnrewrittenMembers"/> assigns one to every
/// such member that no rule rewrites.
/// </summary>
internal enum UnrewrittenReason
{
    /// <summary>Computes its result from the receiver and its arguments, or creates, configures, or releases the receiver.</summary>
    Value,

    /// <summary>Uses or sets the current culture, or converts local time with the host's time zone.</summary>
    CultureOrTimeZone,

    /// <summary>Reads or changes state of the host process or machine.</summary>
    HostState,

    /// <summary>Looks up, configures, reads, or releases an OS process or its handle.</summary>
    Process,

    /// <summary>Runs on the receiver, which the controlled factories supply.</summary>
    Dispatch,

    /// <summary>Reads the host's monotonic clock.</summary>
    RealClock,

    /// <summary>Returns an identifier drawn from a process-wide counter.</summary>
    ProcessWideId,

    /// <summary>Reads or changes the calling thread's state.</summary>
    CurrentThread,

    /// <summary>Reads the BCL object a controlled thread keeps as its identity.</summary>
    IdentityHandle,

    /// <summary>Performs one memory access or fence.</summary>
    Fence,

    /// <summary>Blocks the calling physical thread.</summary>
    Blocks,

    /// <summary>Runs its work through a BCL scheduler, the thread pool, or a new thread rather than the coordinator.</summary>
    Schedules,

    /// <summary>A runtime-async intrinsic.</summary>
    RuntimeAsync,

    /// <summary>Throws on the supported runtime.</summary>
    AlwaysThrows,

    /// <summary>A protected member of a substituted type, which only a derived type can call.</summary>
    SubstitutedTypeProtected,
}
