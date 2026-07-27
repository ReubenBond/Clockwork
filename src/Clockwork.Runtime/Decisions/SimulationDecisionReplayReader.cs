namespace Clockwork.Runtime.Decisions;

/// <summary>
/// <para>
/// The contract a future replay engine (Phase 3+) will implement to feed previously-recorded
/// decisions back for comparison against a live re-run. Phase 2 defines this contract and a
/// simple in-memory implementation (<see cref="SimulationInMemoryDecisionReplayReader"/>) for
/// testing the contract itself - it deliberately does not implement a full scheduler replay
/// engine (that requires the Phase 3 controlled-operation scheduler, which does not exist yet).
/// </para>
/// <para>
/// Readers are expected to be stateful, single-pass, forward-only enumerations - exactly mirroring
/// how decisions were originally recorded (one at a time, in order).
/// </para>
/// </summary>
public interface ISimulationDecisionReplayReader
{
    /// <summary>
    /// Attempts to read the next expected decision from the replay source.
    /// </summary>
    /// <param name="expected">The next expected decision, if one is available.</param>
    /// <returns><see langword="true"/> if a decision was available; <see langword="false"/> if the replay source is exhausted.</returns>
    bool TryGetNext(out SimulationDecisionRecord? expected);
}

/// <summary>
/// A simple <see cref="ISimulationDecisionReplayReader"/> backed by an in-memory list of
/// previously-recorded decisions (typically <see cref="ISimulationDecisionLog.Records"/> from a
/// prior run). Intended for tests and for small in-process replay scenarios; a future
/// file/stream-backed reader can implement the same interface without changing any consumer.
/// </summary>
/// <param name="records">The previously-recorded decisions, in their original order.</param>
public sealed class SimulationInMemoryDecisionReplayReader(IReadOnlyList<SimulationDecisionRecord> records) : ISimulationDecisionReplayReader
{
    private readonly IReadOnlyList<SimulationDecisionRecord> _records = records ?? throw new ArgumentNullException(nameof(records));
    private int _index;

    /// <summary>
    /// Gets the number of records this reader has not yet returned via <see cref="TryGetNext"/>.
    /// </summary>
    public int RemainingCount => _records.Count - _index;

    /// <inheritdoc/>
    public bool TryGetNext(out SimulationDecisionRecord? expected)
    {
        if (_index >= _records.Count)
        {
            expected = null;
            return false;
        }

        expected = _records[_index++];
        return true;
    }
}
