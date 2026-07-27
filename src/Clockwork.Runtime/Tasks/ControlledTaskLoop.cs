namespace Clockwork.Runtime.Tasks;

using Clockwork.Runtime.Scheduling.Resources;

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
/// Ready/wait continuation operations remain deliberately single-threaded: they must happen on the one
/// logical thread the simulation host drives. The deadline registry alone accepts concurrent
/// cancellation, because an external <see cref="CancellationToken"/> callback can remove a controlled
/// timeout while the logical thread is inspecting deadlines. That narrow synchronization does not make
/// continuation execution concurrent. A simulation host owns one loop per node and pumps it from its
/// drive loop; the controlled shims resolve it through an <see cref="ISimulationTaskCoordinator"/>.
/// </para>
/// </summary>
public sealed class ControlledTaskLoop
{
    private readonly Queue<Action> _ready = new();
    private readonly List<WaitEntry> _waits = [];
    private readonly object _deadlineGate = new();

    // Virtual-time deadline registry. Modelled time only ever advances forward, and only when the loop has
    // no ready work and no wait can advance - never on the wall clock - so a finite timeout is a pure,
    // replayable function of the scheduling order rather than a real-time race.
    private readonly List<Deadline> _deadlines = [];
    private TimeSpan _virtualNow;
    private long _deadlineSequence;

    /// <summary>Gets the number of continuations currently runnable (already promoted to the ready queue).</summary>
    public int ReadyCount => _ready.Count;

    /// <summary>Gets the number of readiness-gated continuations still waiting to become runnable.</summary>
    public int WaitingCount => _waits.Count;

    /// <summary>
    /// Gets the loop's modelled time measured from the start of the simulation. It advances only when the
    /// loop drives a virtual-time deadline (see <see cref="RegisterDeadline"/>), never on the wall clock.
    /// </summary>
    public TimeSpan VirtualNow
    {
        get
        {
            lock (_deadlineGate)
            {
                return _virtualNow;
            }
        }
    }

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
    /// Promotes newly-ready waits and executes at most one ready continuation.
    /// </summary>
    /// <returns><see langword="true"/> if a continuation was executed; otherwise, <see langword="false"/>.</returns>
    public bool RunOnce()
    {
        PromoteReady();
        if (_ready.Count == 0)
        {
            return false;
        }

        var continuation = _ready.Dequeue();
        continuation();
        return true;
    }

    /// <summary>
    /// Registers a virtual-time deadline that elapses <paramref name="delay"/> of modelled time from now.
    /// The deadline never consumes real time or a physical timer: it fires only when the loop has nothing
    /// else to run and advances modelled time to it (see <see cref="RunUntil"/> and
    /// <see cref="AdvanceTimeTo"/>). When it fires, <paramref name="onElapsed"/> - if supplied - runs on the
    /// logical thread so a finite wait can record its timeout deterministically.
    /// </summary>
    /// <param name="delay">The strictly positive modelled delay before the deadline elapses.</param>
    /// <param name="onElapsed">An optional callback invoked once when the deadline elapses.</param>
    /// <returns>A handle used to observe elapse or cancel the deadline.</returns>
    public IControlledTimeout RegisterDeadline(TimeSpan delay, Action? onElapsed = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        lock (_deadlineGate)
        {
            var deadline = new Deadline(
                ControlledDeadlineMath.SaturatingAdd(_virtualNow, delay),
                ++_deadlineSequence,
                onElapsed);
            deadline.BindCanceller(CancelDeadline);
            _deadlines.Add(deadline);
            return deadline;
        }
    }

    /// <summary>
    /// Gets the earliest modelled due time (measured from the start of the simulation) among all pending
    /// deadlines, or <see langword="null"/> when none are pending. A host drive loop uses this to fold the
    /// loop's virtual-time deadlines into its cross-queue "advance to the next due time" decision.
    /// </summary>
    /// <returns>The earliest pending deadline's due time, or <see langword="null"/>.</returns>
    public TimeSpan? NextDeadlineDue()
    {
        lock (_deadlineGate)
        {
            TimeSpan? earliest = null;
            foreach (var deadline in _deadlines)
            {
                if (earliest is null || deadline.Due < earliest.Value)
                {
                    earliest = deadline.Due;
                }
            }

            return earliest;
        }
    }

    /// <summary>
    /// Advances the loop's modelled time forward to <paramref name="target"/> (never backwards) and fires
    /// every deadline that is now due, in ascending (due time, registration) order for determinism. Each
    /// fired deadline's callback runs on the logical thread; a callback may complete a controlled task or
    /// enqueue ready work, which the next pump promotes as usual.
    /// </summary>
    /// <param name="target">The modelled time (from the start of the simulation) to advance to.</param>
    public void AdvanceTimeTo(TimeSpan target)
    {
        lock (_deadlineGate)
        {
            if (target > _virtualNow)
            {
                _virtualNow = target;
            }
        }

        while (true)
        {
            Deadline? next = null;
            Action? callback;
            lock (_deadlineGate)
            {
                foreach (var deadline in _deadlines)
                {
                    if (deadline.Due > _virtualNow)
                    {
                        continue;
                    }

                    if (next is null || deadline.Due < next.Due || (deadline.Due == next.Due && deadline.Sequence < next.Sequence))
                    {
                        next = deadline;
                    }
                }

                if (next is null)
                {
                    return;
                }

                _deadlines.Remove(next);
                if (!next.TryClaimElapsed(out callback))
                {
                    continue;
                }
            }

            callback?.Invoke();
        }
    }

    private void CancelDeadline(Deadline deadline)
    {
        lock (_deadlineGate)
        {
            if (deadline.TryCancel())
            {
                _deadlines.Remove(deadline);
            }
        }
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

                // No ready work and nothing can advance yet. If a virtual-time deadline is pending, this is
                // a finite wait paused until a modelled instant, not a deadlock: advance modelled time to the
                // next deadline (firing it, which typically records a timeout or completes a waiter) and
                // re-check. Only a wait with no ready work AND no pending deadline can never be satisfied on
                // this single logical thread, so that alone is the deadlock signature.
                var due = NextDeadlineDue();
                if (due is not null)
                {
                    AdvanceTimeTo(due.Value);
                    continue;
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

    /// <summary>
    /// A pending virtual-time deadline. Immutable except for its elapsed flag; the owning loop removes it
    /// from the registry when it elapses or is cancelled, so a cancelled deadline never lingers or fires.
    /// </summary>
    private sealed class Deadline(TimeSpan due, long sequence, Action? onElapsed) : IControlledTimeout
    {
        private Action? _onElapsed = onElapsed;
        private int _state;

        public TimeSpan Due { get; } = due;

        public long Sequence { get; } = sequence;

        public bool IsElapsed => Volatile.Read(ref _state) == 1;

        public bool TryClaimElapsed(out Action? callback)
        {
            if (_state != 0)
            {
                callback = null;
                return false;
            }

            Volatile.Write(ref _state, 1);
            callback = _onElapsed;
            _onElapsed = null;
            return true;
        }

        public bool TryCancel()
        {
            if (_state != 0)
            {
                return false;
            }

            Volatile.Write(ref _state, 2);
            _onElapsed = null;
            return true;
        }

        public void Cancel() => _canceller?.Invoke(this);

        internal void BindCanceller(Action<Deadline> canceller) => _canceller = canceller;

        private Action<Deadline>? _canceller;
    }
}
