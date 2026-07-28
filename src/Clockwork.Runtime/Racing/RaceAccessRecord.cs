using System.Collections.Immutable;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Racing;

/// <summary>One operation's read or write of a logical memory location.</summary>
/// <param name="OperationId">The accessing controlled operation.</param>
/// <param name="Kind">The read/write kind.</param>
/// <param name="Location">The logical location.</param>
/// <param name="Source">The exact injected call site.</param>
/// <param name="SynchronizationContext">The controlled synchronization held at the access.</param>
public sealed record RaceAccessRecord(
    ControlledOperationId OperationId,
    RaceAccessKind Kind,
    RaceMemoryLocation Location,
    RaceSourceLocation Source,
    ImmutableArray<string> SynchronizationContext);
