using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;

namespace Clockwork.Runtime.Decisions;

/// <summary>
/// The information a caller supplies to <see cref="ISimulationDecisionLog.Record"/> to record one
/// deterministic decision. <see cref="ISimulationDecisionLog"/> assigns the monotonic
/// <see cref="SimulationDecisionRecord.Id"/> - callers do not (and cannot) choose it.
/// </summary>
/// <param name="Domain">Which independent seed/decision domain this decision belongs to.</param>
/// <param name="Kind">The broad category of decision.</param>
/// <param name="SourceId">
/// An optional stable identifier for the call site/source that made this decision (e.g. a node
/// address, a stable tag for the code path) - used to correlate decisions across runs
/// independently of registration/call order.
/// </param>
/// <param name="InputMetadata">
/// An optional, human-readable description of the input/range the decision was drawn from (e.g.
/// <c>"[0, 10)"</c> for a bounded random draw, or the candidate set for a <see cref="SimulationDecisionKind.Choice"/>).
/// </param>
/// <param name="SelectedResult">
/// A stable string representation of the value that was actually selected/decided. This is what
/// replay validation compares against a prior recording.
/// </param>
/// <param name="RuntimeId">The active simulation runtime's identity at the time of the decision.</param>
/// <param name="NodeId">
/// The active node's address at the time of the decision, or <see langword="null"/> for
/// cluster-level (non-node-scoped) decisions.
/// </param>
/// <param name="LogicalExecutionId">
/// The active logical execution identity placeholder at the time of the decision; see
/// <see cref="SimulationLogicalExecutionId"/>.
/// </param>
public readonly record struct SimulationDecisionRequest(
    SimulationSeedDomain Domain,
    SimulationDecisionKind Kind,
    string? SourceId,
    string? InputMetadata,
    string SelectedResult,
    Guid RuntimeId,
    string? NodeId,
    SimulationLogicalExecutionId LogicalExecutionId);

/// <summary>
/// One recorded, immutable deterministic decision: everything in <see cref="SimulationDecisionRequest"/>
/// plus the monotonic <see cref="Id"/> assigned by the log that recorded it.
/// </summary>
/// <param name="Id">The decision's position in the log that recorded it.</param>
/// <param name="Domain">Which independent seed/decision domain this decision belongs to.</param>
/// <param name="Kind">The broad category of decision.</param>
/// <param name="SourceId">An optional stable identifier for the call site/source.</param>
/// <param name="InputMetadata">An optional, human-readable description of the input/range.</param>
/// <param name="SelectedResult">A stable string representation of the selected value.</param>
/// <param name="RuntimeId">The active simulation runtime's identity at the time of the decision.</param>
/// <param name="NodeId">The active node's address, or <see langword="null"/> if not node-scoped.</param>
/// <param name="LogicalExecutionId">The active logical execution identity placeholder.</param>
public sealed record SimulationDecisionRecord(
    SimulationDecisionId Id,
    SimulationSeedDomain Domain,
    SimulationDecisionKind Kind,
    string? SourceId,
    string? InputMetadata,
    string SelectedResult,
    Guid RuntimeId,
    string? NodeId,
    SimulationLogicalExecutionId LogicalExecutionId);
