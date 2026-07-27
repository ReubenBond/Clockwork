using System.Globalization;
using Clockwork.Runtime.Decisions;

namespace Clockwork.Runtime.Scheduling.Strategies;

/// <summary>
/// <para>
/// Reproduces a previously recorded schedule exactly by replaying the
/// <see cref="SimulationDecisionKind.SchedulingOrder"/> decisions captured during an earlier run
/// (typically produced by <see cref="SeededRandomSchedulingStrategy"/> with a decision log attached).
/// On each real choice it consumes the next recorded selection and grants the baton to the operation
/// whose id matches it.
/// </para>
/// <para>
/// It fails fast at the <em>first</em> divergence - if the recorded log is exhausted before the run
/// finishes making choices, or the recorded operation is not currently runnable - by throwing
/// <see cref="SimulationDecisionReplayMismatchException"/>, exactly as the Phase 2 replay contract
/// requires. This is the scheduler-side counterpart of
/// <see cref="SimulationDecisionReplayValidator"/>: the validator checks decisions made <em>inside</em>
/// operations, while this drives the scheduling decisions themselves.
/// </para>
/// </summary>
public sealed class ReplaySchedulingStrategy : IControlledSchedulingStrategy
{
    private readonly List<SimulationDecisionRecord> _scheduling;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplaySchedulingStrategy"/> class from a recorded
    /// decision sequence. Only <see cref="SimulationDecisionKind.SchedulingOrder"/> records are used;
    /// any other decisions (e.g. those recorded inside operation bodies) are ignored, so the same log
    /// can be passed here and to a <see cref="SimulationDecisionReplayValidator"/>.
    /// </summary>
    /// <param name="recordedDecisions">The decisions recorded during the original run, in order.</param>
    public ReplaySchedulingStrategy(IReadOnlyList<SimulationDecisionRecord> recordedDecisions)
    {
        ArgumentNullException.ThrowIfNull(recordedDecisions);
        var scheduling = new List<SimulationDecisionRecord>(recordedDecisions.Count);
        foreach (var record in recordedDecisions)
        {
            if (record.Kind == SimulationDecisionKind.SchedulingOrder)
            {
                scheduling.Add(record);
            }
        }

        _scheduling = scheduling;
    }

    /// <inheritdoc/>
    public string Name => "replay";

    /// <inheritdoc/>
    public bool RecordsNondeterministicChoices => false;

    /// <inheritdoc/>
    public ControlledOperation ChooseNext(ControlledSchedulingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Runnable.Count == 1)
        {
            // Single-candidate steps were never recorded, so they must not consume the replay stream.
            return context.Runnable[0];
        }

        if (_index >= _scheduling.Count)
        {
            throw new SimulationDecisionReplayMismatchException(
                "Replay diverged: the recorded scheduling log is exhausted, but the scheduler still has " +
                $"a choice among {context.Runnable.Count} runnable operations to make.");
        }

        var expected = _scheduling[_index++];
        foreach (var operation in context.Runnable)
        {
            if (string.Equals(FormatId(operation.Id), expected.SelectedResult, StringComparison.Ordinal))
            {
                return operation;
            }
        }

        throw new SimulationDecisionReplayMismatchException(
            $"Replay diverged at scheduling decision {expected.Id}: the recorded run selected operation " +
            $"'{expected.SelectedResult}', but it is not among the currently runnable operations " +
            $"[{FormatCandidates(context.Runnable)}].");
    }

    internal static string FormatId(ControlledOperationId id) =>
        id.Value.ToString(CultureInfo.InvariantCulture);

    private static string FormatCandidates(IReadOnlyList<ControlledOperation> runnable)
    {
        var ids = new string[runnable.Count];
        for (var i = 0; i < runnable.Count; i++)
        {
            ids[i] = FormatId(runnable[i].Id);
        }

        return string.Join(",", ids);
    }
}
