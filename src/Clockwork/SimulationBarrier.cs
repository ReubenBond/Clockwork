using System.Diagnostics;
using System.Globalization;

namespace Clockwork;

/// <summary>
/// <para>
/// A cyclic rendezvous barrier for simulation code, modeled on <see cref="System.Threading.Barrier"/>:
/// a fixed number of participants each call <see cref="ArriveAndWaitAsync"/>, and none of them are
/// released until all have arrived. Once every participant has arrived, they are all released
/// together for that round, and the barrier immediately resets to accept the next round.
/// </para>
/// <para>
/// A participant that is canceled while waiting retracts its arrival - <see cref="ArrivedCount"/>
/// is decremented back down - so a canceled wait never silently counts toward releasing the
/// others.
/// </para>
/// <para>
/// Completions are dispatched through the supplied <see cref="SimulationTaskQueue"/>, never
/// inline and never via a real-time wait or thread-pool callback, so release order is
/// deterministic and matches the order in which participants arrived.
/// </para>
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SimulationBarrier
{
    private readonly SimulationTaskQueue _queue;
    private readonly List<TaskCompletionSource> _waiters = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationBarrier"/> class.
    /// </summary>
    /// <param name="queue">The queue used to dispatch waiter completions deterministically.</param>
    /// <param name="participantCount">The fixed number of participants required to release each round. Must be positive.</param>
    /// <param name="name">An optional name for diagnostics (for example, in a debugger).</param>
    public SimulationBarrier(SimulationTaskQueue queue, int participantCount, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentOutOfRangeException.ThrowIfLessThan(participantCount, 1);
        _queue = queue;
        ParticipantCount = participantCount;
        Name = name;
    }

    /// <summary>
    /// Gets the optional name of this barrier, for diagnostics.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the fixed number of participants required to release each round.
    /// </summary>
    public int ParticipantCount { get; }

    /// <summary>
    /// Gets the number of participants that have arrived for the current round but not yet been released.
    /// </summary>
    public int ArrivedCount { get; private set; }

    /// <summary>
    /// Arrives at the barrier and waits for every other participant to arrive. When the last
    /// participant arrives, every waiter for that round - including this call - is released
    /// together, and the barrier resets for the next round.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token whose cancellation retracts this arrival (decrementing <see cref="ArrivedCount"/>
    /// back down, so it no longer counts toward releasing the round) and transitions the returned
    /// task to the canceled state. If already canceled at the time of the call, returns a
    /// canceled task without arriving at all (<see cref="ArrivedCount"/> is left unchanged).
    /// </param>
    /// <returns>A task that completes when this round is released.</returns>
    public Task ArriveAndWaitAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var task = SimulationRendezvousSupport.AddWaiter(_waiters, cancellationToken, onCancelled: () => ArrivedCount--);
        ArrivedCount++;

        if (ArrivedCount == ParticipantCount)
        {
            ArrivedCount = 0;
            SimulationRendezvousSupport.ReleaseAll(_queue, _waiters);
        }

        return task;
    }

    private string DebuggerDisplay => string.Create(
        CultureInfo.InvariantCulture,
        $"SimulationBarrier({Name ?? "unnamed"}, Arrived={ArrivedCount}/{ParticipantCount})");
}
