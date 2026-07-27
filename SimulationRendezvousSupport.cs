namespace Clockwork;

/// <summary>
/// Shared waiter bookkeeping for the rendezvous primitives (<see cref="SimulationGate"/>,
/// <see cref="SimulationLatch"/>, <see cref="SimulationBarrier"/>): registering a cancelable
/// waiter and releasing a batch of waiters deterministically through a <see cref="SimulationTaskQueue"/>.
/// Not public - each primitive exposes its own crisp, purpose-specific API; this only factors out
/// the mechanics they all share so those mechanics are implemented (and tested, transitively) once.
/// </summary>
internal static class SimulationRendezvousSupport
{
    /// <summary>
    /// Registers a new waiter in <paramref name="waiters"/> and returns its task. If
    /// <paramref name="cancellationToken"/> is already canceled, returns a canceled task without
    /// registering anything (per Clockwork's determinism requirements: cancellation is observed
    /// synchronously, never via a background wait). Otherwise, if the token can be canceled, a
    /// synchronous cancellation callback removes the waiter from <paramref name="waiters"/> (if it
    /// is still present - it may already have been released) and transitions its task to canceled.
    /// </summary>
    /// <param name="waiters">The list of pending waiters to register into.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <param name="onCancelled">
    /// Optional callback invoked when this specific waiter is canceled while still pending (i.e.
    /// it was found and removed from <paramref name="waiters"/>). Used by <see cref="SimulationBarrier"/>
    /// to retract an arrival.
    /// </param>
    /// <returns>The pending (or already-canceled) waiter's task.</returns>
    public static Task AddWaiter(List<TaskCompletionSource> waiters, CancellationToken cancellationToken, Action? onCancelled = null)
    {
        ArgumentNullException.ThrowIfNull(waiters);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var tcs = new TaskCompletionSource();
        waiters.Add(tcs);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (waiters.Remove(tcs))
                {
                    onCancelled?.Invoke();
                }

                tcs.TrySetCanceled(cancellationToken);
            });
        }

        return tcs.Task;
    }

    /// <summary>
    /// Snapshots and clears <paramref name="waiters"/>, then enqueues completion of each one onto
    /// <paramref name="queue"/> in the order they were registered, so continuations run at a
    /// deterministic point in the simulation's schedule (never inline, never via a real-time wait
    /// or thread-pool callback) and observe a stable, FIFO release order.
    /// </summary>
    /// <param name="queue">The queue to dispatch completions through.</param>
    /// <param name="waiters">The waiters to release.</param>
    public static void ReleaseAll(SimulationTaskQueue queue, List<TaskCompletionSource> waiters)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(waiters);

        if (waiters.Count == 0)
        {
            return;
        }

        var snapshot = waiters.ToArray();
        waiters.Clear();
        foreach (var waiter in snapshot)
        {
            queue.Enqueue(new ScheduledActionItem(() => waiter.TrySetResult()));
        }
    }
}
