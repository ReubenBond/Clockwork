namespace Clockwork.Runtime.Tasks;

/// <summary>
/// <para>
/// A self-contained, deterministic, single-threaded work loop that backs the controlled async/task
/// machinery. It holds a FIFO ready queue of runnable continuations and a wait list of readiness-gated
/// continuations (an <c>await</c> of an incomplete controlled task registers here). Pumping promotes
/// any wait whose readiness predicate now holds into the ready queue, then runs exactly one ready
/// continuation - so the whole model is a cooperative message loop with no physical thread ever blocked
/// on real time and no busy-spin.
/// </para>
/// <para>
/// The loop is deliberately <b>not</b> thread-safe: every operation must happen on the one logical
/// thread the simulation host drives it from. That single-threaded discipline is the whole point - it
/// is what makes continuation ordering a pure function of the deterministic insertion order, and it is
/// what lets a stalled synchronous wait be recognised immediately as a deadlock instead of a real-time
/// hang. A simulation host owns one loop per node and pumps it from its drive loop; the controlled
/// shims resolve it through an <see cref="ISimulationTaskCoordinator"/>.
/// </para>
/// </summary>
public sealed class ControlledTaskLoop
{
    private readonly Queue<Action> _ready = new();
    private readonly List<WaitEntry> _waits = [];

    /// <summary>Gets the number of continuations currently runnable (already promoted to the ready queue).</summary>
    public int ReadyCount => _ready.Count;

    /// <summary>Gets the number of readiness-gated continuations still waiting to become runnable.</summary>
    public int WaitingCount => _waits.Count;

    /// <summary>Gets a value indicating whether the loop has no ready and no waiting work left.</summary>
    public bool IsIdle => _ready.Count == 0 && _waits.Count == 0;

    /// <summary>Enqueues a continuation that is runnable immediately, ordered after existing ready work.</summary>
    /// <param name="continuation">The continuation to enqueue.</param>
    public void Schedule(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        _ready.Enqueue(continuation);
    }

    /// <summary>
    /// Registers a continuation to run once <paramref name="isReady"/> holds. The predicate is evaluated
    /// each time the loop pumps; when it first returns <see langword="true"/> the continuation is promoted
    /// to the ready queue and the entry removed.
    /// </summary>
    /// <param name="isReady">The readiness predicate.</param>
    /// <param name="continuation">The continuation to run once ready.</param>
    public void ScheduleWhenReady(Func<bool> isReady, Action continuation)
    {
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(continuation);
        _waits.Add(new WaitEntry(isReady, continuation));
    }

    /// <summary>
    /// Pumps the loop until <paramref name="completed"/> returns <see langword="true"/>. Each iteration
    /// promotes newly-ready waits and runs one ready continuation. If the ready queue empties and no wait
    /// can advance yet <paramref name="completed"/> is still <see langword="false"/>, the wait can never be
    /// satisfied on this single logical thread, so a <see cref="ControlledSynchronousWaitDeadlockException"/>
    /// is thrown.
    /// </summary>
    /// <param name="completed">The predicate that ends the pump.</param>
    /// <param name="apiName">The controlled API driving the pump, for the deadlock diagnostic.</param>
    public void RunUntil(Func<bool> completed, string apiName)
    {
        ArgumentNullException.ThrowIfNull(completed);
        while (true)
        {
            if (completed())
            {
                return;
            }

            PromoteReady();
            if (_ready.Count == 0)
            {
                if (completed())
                {
                    return;
                }

                throw new ControlledSynchronousWaitDeadlockException(apiName);
            }

            var continuation = _ready.Dequeue();
            continuation();
        }
    }

    /// <summary>
    /// Pumps the loop until it is idle (no ready and no waiting work), or until <paramref name="completed"/>
    /// holds. Unlike <see cref="RunUntil"/> this never throws on a stall: reaching idle with unsatisfied
    /// waits is reported to the caller via <see cref="WaitingCount"/> rather than treated as a deadlock,
    /// so a host drive loop can decide how to classify leftover incomplete work.
    /// </summary>
    /// <param name="completed">An optional early-stop predicate; may be <see langword="null"/>.</param>
    /// <returns>The number of continuations executed.</returns>
    public int RunUntilIdle(Func<bool>? completed = null)
    {
        var executed = 0;
        while (true)
        {
            if (completed is not null && completed())
            {
                return executed;
            }

            PromoteReady();
            if (_ready.Count == 0)
            {
                return executed;
            }

            var continuation = _ready.Dequeue();
            continuation();
            executed++;
        }
    }

    private void PromoteReady()
    {
        // Promote every wait whose readiness now holds, preserving insertion order, before running any
        // continuation. Evaluated fresh each pump so a wait gated on a task completed by an earlier
        // continuation becomes runnable on the very next iteration.
        for (var i = 0; i < _waits.Count;)
        {
            if (_waits[i].IsReady())
            {
                var entry = _waits[i];
                _waits.RemoveAt(i);
                _ready.Enqueue(entry.Continuation);
            }
            else
            {
                i++;
            }
        }
    }

    private readonly record struct WaitEntry(Func<bool> IsReady, Action Continuation);
}
