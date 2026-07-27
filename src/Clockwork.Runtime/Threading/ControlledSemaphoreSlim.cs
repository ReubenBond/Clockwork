using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="SemaphoreSlim"/> surface. <see cref="SemaphoreSlim"/> is sealed, so -
/// exactly as with the controlled <see cref="System.Threading.Thread"/> surface - the controlled object
/// <em>is</em> a real <see cref="SemaphoreSlim"/> used purely as an identity handle, and the rewriter
/// redirects <c>new SemaphoreSlim(...)</c> to <see cref="Create(int)"/>/<see cref="Create(int, int)"/>
/// and each instance member to a static method here whose first parameter is the receiver.
/// </para>
/// <para>
/// Inside a simulation the count and waiter set are modelled directly on the single cooperative logical
/// thread: <see cref="CurrentCount"/> reads the modelled count, a synchronous <c>Wait</c> pumps the
/// deterministic loop until a permit is available (or the wait is cancelled), and <c>WaitAsync</c>
/// returns a task that a <c>Release</c> completes on the logical thread. Waiters are served in
/// deterministic arrival (FIFO) order - a replayable selection, though the BCL makes no fairness promise.
/// Cancellation is honoured synchronously through
/// <see cref="CancellationToken.Register(System.Action)"/> (whose callback runs on the cancelling logical
/// thread). No permit wait ever blocks a physical thread or consumes real time; a wait that can never be
/// satisfied surfaces as the standard controlled deadlock diagnostic. Outside a simulation every shim
/// delegates to the real <see cref="SemaphoreSlim"/>.
/// </para>
/// <para>
/// <see cref="SemaphoreSlim.AvailableWaitHandle"/> materialises a real
/// <see cref="System.Threading.WaitHandle"/> - an OS synchronization primitive owned by Phase 7B - so it
/// is rejected precisely (<see cref="ControlledSemaphoreSlimUnsupportedException"/>) until that phase
/// provides a controlled wait handle.
/// </para>
/// <para>
/// Per-instance state is held with weak keys (<see cref="ConditionalWeakTable{TKey,TValue}"/>) so the
/// association never keeps a semaphore alive. This mirrors Microsoft Coyote's controlled
/// <c>SemaphoreSlim</c> (MIT-licensed). Finite timeouts use the deterministic virtual-time deadline
/// engine, so a finite <c>Wait</c>/<c>WaitAsync</c> returns <see langword="false"/> on the simulated
/// deadline with release-vs-timeout-vs-cancellation resolved by the first-winner policy - never real time.
/// </para>
/// </summary>
public static class ControlledSemaphoreSlim
{
    private const string WaitApi = "System.Threading.SemaphoreSlim.Wait";
    private const string AvailableWaitHandleApi = "System.Threading.SemaphoreSlim.get_AvailableWaitHandle";

    private sealed class Waiter
    {
        public readonly TaskCompletionSource<bool> Completion = new();

        public CancellationTokenRegistration Registration;

        // The virtual-time deadline for a finite wait, or null for an infinite wait. Cancelled when the
        // waiter completes for any other reason (permit served or cancellation) so a stale timeout cannot
        // fire; when it elapses it completes the waiter with false.
        public IControlledTimeout? Deadline;
    }

    private sealed class State
    {
        public State(int count, int maxCount)
        {
            Count = count;
            MaxCount = maxCount;
        }

        public int Count { get; set; }

        public int MaxCount { get; }

        public bool Disposed { get; set; }

        // Waiters blocked for a permit, in arrival order. Release serves the front waiters.
        public List<Waiter> Waiters { get; } = new();
    }

    private static readonly ConditionalWeakTable<SemaphoreSlim, State> States = new();

    private static State StateOf(SemaphoreSlim instance) =>
        States.GetValue(instance, s => new State(s.CurrentCount, int.MaxValue));

    /// <summary>Controlled <c>new SemaphoreSlim(int)</c>.</summary>
    /// <param name="initialCount">The initial number of permits.</param>
    /// <returns>A real semaphore object used as the controlled identity handle.</returns>
    public static SemaphoreSlim Create(int initialCount)
    {
        var instance = new SemaphoreSlim(initialCount);
        States.AddOrUpdate(instance, new State(initialCount, int.MaxValue));
        return instance;
    }

    /// <summary>Controlled <c>new SemaphoreSlim(int, int)</c>.</summary>
    /// <param name="initialCount">The initial number of permits.</param>
    /// <param name="maxCount">The maximum number of permits.</param>
    /// <returns>A real semaphore object used as the controlled identity handle.</returns>
    public static SemaphoreSlim Create(int initialCount, int maxCount)
    {
        var instance = new SemaphoreSlim(initialCount, maxCount);
        States.AddOrUpdate(instance, new State(initialCount, maxCount));
        return instance;
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.CurrentCount"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>The number of permits currently available.</returns>
    public static int CurrentCount(SemaphoreSlim instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.CurrentCount;
        }

        return StateOf(instance).Count;
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    public static void Wait(SemaphoreSlim instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            instance.Wait();
            return;
        }

        WaitControlled(instance, Timeout.Infinite, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    public static void Wait(SemaphoreSlim instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            instance.Wait(cancellationToken);
            return;
        }

        WaitControlled(instance, Timeout.Infinite, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(int)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">Zero tries without blocking; -1 blocks indefinitely; a finite positive value blocks until a permit is available or the simulated deadline elapses.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, int millisecondsTimeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.Wait(millisecondsTimeout);
        }

        return WaitControlled(instance, millisecondsTimeout, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(int, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.Wait(millisecondsTimeout, cancellationToken);
        }

        return WaitControlled(instance, millisecondsTimeout, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.Wait(timeout);
        }

        return WaitControlled(instance, ToMilliseconds(timeout), CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(TimeSpan, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.Wait(timeout, cancellationToken);
        }

        return WaitControlled(instance, ToMilliseconds(timeout), cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>A task that completes when a permit is acquired.</returns>
    public static Task WaitAsync(SemaphoreSlim instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync();
        }

        return WaitAsyncControlled(instance, Timeout.Infinite, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task that completes when a permit is acquired.</returns>
    public static Task WaitAsync(SemaphoreSlim instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync(cancellationToken);
        }

        return WaitAsyncControlled(instance, Timeout.Infinite, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(int)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, int millisecondsTimeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync(millisecondsTimeout);
        }

        return WaitAsyncControlled(instance, millisecondsTimeout, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(int, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync(millisecondsTimeout, cancellationToken);
        }

        return WaitAsyncControlled(instance, millisecondsTimeout, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync(timeout);
        }

        return WaitAsyncControlled(instance, ToMilliseconds(timeout), CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(TimeSpan, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitAsync(timeout, cancellationToken);
        }

        return WaitAsyncControlled(instance, ToMilliseconds(timeout), cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Release()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>The permit count before the release.</returns>
    public static int Release(SemaphoreSlim instance) => Release(instance, 1);

    /// <summary>Controlled <see cref="SemaphoreSlim.Release(int)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="releaseCount">The number of permits to add back.</param>
    /// <returns>The permit count before the release.</returns>
    public static int Release(SemaphoreSlim instance, int releaseCount)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.Release(releaseCount);
        }

        if (releaseCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseCount), releaseCount, "The release count must be greater than zero.");
        }

        var state = StateOf(instance);
        ThrowIfDisposed(state);

        var previous = state.Count;
        if ((long)state.Count + releaseCount > state.MaxCount)
        {
            throw new SemaphoreFullException();
        }

        state.Count += releaseCount;

        // Serve waiters in arrival order: each served waiter consumes one permit and completes its task
        // synchronously on this logical thread, which the deterministic loop observes to resume it.
        while (state.Waiters.Count > 0 && state.Count > 0)
        {
            var waiter = state.Waiters[0];
            state.Waiters.RemoveAt(0);
            waiter.Registration.Dispose();
            waiter.Deadline?.Cancel();
            state.Count--;
            waiter.Completion.TrySetResult(true);
        }

        return previous;
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Dispose()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    public static void Dispose(SemaphoreSlim instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            instance.Dispose();
            return;
        }

        StateOf(instance).Disposed = true;
    }

    /// <summary>Rejected controlled <see cref="SemaphoreSlim.AvailableWaitHandle"/> (depends on Phase 7B wait handles).</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>Never returns inside a simulation; throws instead.</returns>
    public static WaitHandle AvailableWaitHandle(SemaphoreSlim instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.AvailableWaitHandle;
        }

        throw new ControlledSemaphoreSlimUnsupportedException(
            AvailableWaitHandleApi,
            "it materialises a real OS WaitHandle, which belongs to the controlled wait-handle infrastructure " +
            "landing in Phase 7B; until then a controlled semaphore cannot expose one without escaping the " +
            "deterministic scheduler.");
    }

    private static bool WaitControlled(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ValidateTimeout(millisecondsTimeout);
        var state = StateOf(instance);
        ThrowIfDisposed(state);
        cancellationToken.ThrowIfCancellationRequested();

        if (state.Count > 0)
        {
            state.Count--;
            return true;
        }

        if (millisecondsTimeout == 0)
        {
            return false;
        }

        var waiter = Enqueue(state, millisecondsTimeout, cancellationToken);
        ControlledTaskRuntime.DrainUntil(() => waiter.Completion.Task.IsCompleted, WaitApi);

        // The waiter completes as served (true), timed-out (false), or cancelled. GetResult rethrows the
        // OperationCanceledException for a cancelled waiter, so synchronous cancellation observes the same
        // exception as the real SemaphoreSlim; a timeout returns false.
        return waiter.Completion.Task.GetAwaiter().GetResult();
    }

    private static Task<bool> WaitAsyncControlled(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        try
        {
            ValidateTimeout(millisecondsTimeout);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Task.FromException<bool>(exception);
        }

        var state = StateOf(instance);
        if (state.Disposed)
        {
            return Task.FromException<bool>(new ObjectDisposedException(nameof(SemaphoreSlim)));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        if (state.Count > 0)
        {
            state.Count--;
            return Task.FromResult(true);
        }

        if (millisecondsTimeout == 0)
        {
            return Task.FromResult(false);
        }

        var waiter = Enqueue(state, millisecondsTimeout, cancellationToken);
        return waiter.Completion.Task;
    }

    private static Waiter Enqueue(State state, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        var waiter = new Waiter();
        state.Waiters.Add(waiter);
        if (cancellationToken.CanBeCanceled)
        {
            // The registration fires synchronously on whichever logical strand cancels the token, so a
            // still-queued waiter is removed and cancelled deterministically without any real timer.
            waiter.Registration = cancellationToken.Register(() =>
            {
                if (state.Waiters.Remove(waiter))
                {
                    waiter.Deadline?.Cancel();
                    waiter.Completion.TrySetCanceled(cancellationToken);
                }
            });
        }

        if (millisecondsTimeout != Timeout.Infinite)
        {
            // A finite wait registers a deterministic virtual-time deadline. It elapses only when the loop
            // has no other runnable work and advances modelled time to it, so a permit that could be served
            // now (Release) or a cancellation possible now always wins over the timeout - the first-winner
            // policy - and a timeout completes the waiter with false.
            waiter.Deadline = ControlledTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout),
                onElapsed: () =>
                {
                    if (state.Waiters.Remove(waiter))
                    {
                        waiter.Registration.Dispose();
                        waiter.Completion.TrySetResult(false);
                    }
                },
                WaitApi);
        }

        return waiter;
    }

    private static void ThrowIfDisposed(State state)
    {
        ObjectDisposedException.ThrowIf(state.Disposed, typeof(SemaphoreSlim));
    }

    private static void ValidateTimeout(int millisecondsTimeout)
    {
        if (millisecondsTimeout < Timeout.Infinite)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, "The timeout must be -1 (infinite) or a non-negative value.");
        }
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        var total = (long)timeout.TotalMilliseconds;
        if (total < Timeout.Infinite || total > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be between -1 and Int32.MaxValue milliseconds.");
        }

        return (int)total;
    }
}
