using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;
using Clockwork.Runtime.Racing;

namespace Clockwork.Shims.System.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="SemaphoreSlim"/> surface. <see cref="SemaphoreSlim"/> is sealed, so -
/// exactly as with the controlled <see cref="global::System.Threading.Thread"/> surface - the controlled object
/// <em>is</em> a real <see cref="SemaphoreSlim"/> used purely as an identity handle, and the rewriter
/// redirects <c>new SemaphoreSlim(...)</c> to <see cref="Create(int)"/>/<see cref="Create(int, int)"/>
/// and each instance member to a static method here whose first parameter is the receiver.
/// </para>
/// <para>
/// Inside a simulation the count and waiter set are modelled under a per-semaphore gate: a synchronous
/// <c>Wait</c> pumps the deterministic loop until a permit is available (or the wait is cancelled), and
/// <c>WaitAsync</c> returns a task completed by the first serialized release, timeout, cancellation, or
/// disposal. Waiters are served in deterministic arrival (FIFO) order - a replayable selection, though
/// the BCL makes no fairness promise. Cancellation is honoured synchronously through
/// <see cref="CancellationToken.Register(global::System.Action)"/>; callbacks may arrive on external threads, so
/// waiter mutation is serialized and cancellation registrations are always disposed outside the gate.
/// No permit wait consumes real time.
/// </para>
/// <para>
/// <see cref="SemaphoreSlim.AvailableWaitHandle"/> is bridged to a controlled manual-reset wait handle
///: the handle is materialised once, cached, and its signalled state tracks whether a permit
/// is available (count &gt; 0) across every <c>Wait</c>/<c>Release</c> transition. Observing it never
/// consumes a permit, and it composes with <c>WaitAny</c>/<c>WaitAll</c> across the controlled surface.
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

    private sealed class Waiter
    {
        public readonly TaskCompletionSource<bool> Completion = new();

        public CancellationTokenRegistration Registration;

        // The virtual-time deadline for a finite wait, or null for an infinite wait. Cancelled when the
        // waiter completes for any other reason (permit served or cancellation) so a stale timeout cannot
        // fire; when it elapses it completes the waiter with false.
        public ISimulationTimer? Deadline;

        public WaiterOutcome Outcome;
    }

    private sealed class State
    {
        public State(int count, int maxCount)
        {
            _count = count;
            MaxCount = maxCount;
        }

        private int _count;

        // Mutating the modelled count immediately republishes the AvailableWaitHandle bridge (if one has
        // been materialised) so its signalled state tracks "a permit is available" (count > 0) across every
        // Wait/Release transition without instrumenting each mutation site.
        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                if (AvailableHandle is not null)
                {
                    ControlledWaitHandle.UpdateBridgeSignal(AvailableHandle, _count > 0);
                }
            }
        }

        public int MaxCount { get; }

        public bool Disposed { get; set; }

        // The lazily-materialised AvailableWaitHandle bridge (a controlled manual-reset handle), or null
        // until first observed. Kept for the lifetime of the semaphore's modelled state.
        public WaitHandle? AvailableHandle { get; set; }

        public object Gate { get; } = new();

        // Waiters blocked for a permit, in arrival order. Release serves the front waiters.
        public List<Waiter> Waiters { get; } = new();
    }

    private enum WaiterOutcome
    {
        Pending,
        Signaled,
        TimedOut,
        Canceled,
        Disposed,
    }

    private readonly record struct WaiterCleanup(
        Waiter Waiter,
        WaiterOutcome Outcome,
        CancellationTokenRegistration Registration,
        ISimulationTimer? Deadline,
        CancellationToken CancellationToken);

    private static readonly AsyncLocal<Action?> CancellationRegistrationHook = new();

    internal static Action? BeforeCancellationRegistrationForTesting
    {
        get => CancellationRegistrationHook.Value;
        set => CancellationRegistrationHook.Value = value;
    }

    private static readonly ConditionalWeakTable<SemaphoreSlim, State> States = new();

    private static State StateOf(SemaphoreSlim instance, string apiName)
    {
        if (States.TryGetValue(instance, out State? state))
        {
            return state;
        }

        throw new SimulationApiException(
            SimulationApiCategory.SemaphoreSlim,
            apiName,
            "the semaphore was not created through the controlled SemaphoreSlim surface, so its maximum " +
            "count and controlled waiter state are unknown.");
    }

    /// <summary>Controlled <c>new SemaphoreSlim(int)</c>.</summary>
    /// <param name="initialCount">The initial number of permits.</param>
    /// <returns>A real semaphore object used as the controlled identity handle.</returns>
    public static SemaphoreSlim Create(int initialCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim..ctor");
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim..ctor");
        var instance = new SemaphoreSlim(initialCount, maxCount);
        States.AddOrUpdate(instance, new State(initialCount, maxCount));
        return instance;
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.CurrentCount"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>The number of permits currently available.</returns>
    public static int CurrentCount(SemaphoreSlim instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.get_CurrentCount");
        ArgumentNullException.ThrowIfNull(instance);
        var state = StateOf(instance, "System.Threading.SemaphoreSlim.get_CurrentCount");
        lock (state.Gate)
        {
            return state.Count;
        }
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    public static void Wait(SemaphoreSlim instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        WaitControlled(instance, Timeout.Infinite, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    public static void Wait(SemaphoreSlim instance, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        WaitControlled(instance, Timeout.Infinite, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(int)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">Zero tries without blocking; -1 blocks indefinitely; a finite positive value blocks until a permit is available or the simulated deadline elapses.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, millisecondsTimeout, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(int, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, millisecondsTimeout, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, ToMilliseconds(timeout), CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Wait(TimeSpan, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns><see langword="true"/> if a permit was acquired.</returns>
    public static bool Wait(SemaphoreSlim instance, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, ToMilliseconds(timeout), cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>A task that completes when a permit is acquired.</returns>
    public static Task WaitAsync(SemaphoreSlim instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
        return WaitAsyncControlled(instance, Timeout.Infinite, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task that completes when a permit is acquired.</returns>
    public static Task WaitAsync(SemaphoreSlim instance, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
        return WaitAsyncControlled(instance, Timeout.Infinite, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(int)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
        return WaitAsyncControlled(instance, millisecondsTimeout, CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(int, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="millisecondsTimeout">The timeout, interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
        return WaitAsyncControlled(instance, millisecondsTimeout, cancellationToken);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
        return WaitAsyncControlled(instance, ToMilliseconds(timeout), CancellationToken.None);
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.WaitAsync(TimeSpan, CancellationToken)"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <param name="timeout">The timeout, converted to milliseconds and interpreted as for <see cref="Wait(SemaphoreSlim, int)"/>.</param>
    /// <param name="cancellationToken">A token that aborts the wait.</param>
    /// <returns>A task whose result indicates whether a permit was acquired.</returns>
    public static Task<bool> WaitAsync(SemaphoreSlim instance, TimeSpan timeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.WaitAsync");
        ArgumentNullException.ThrowIfNull(instance);
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
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.Release");
        ArgumentNullException.ThrowIfNull(instance);
        if (releaseCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseCount), releaseCount, "The release count must be greater than zero.");
        }

        var state = StateOf(instance, "System.Threading.SemaphoreSlim.Release");
        List<WaiterCleanup> completed = [];
        int previous;
        lock (state.Gate)
        {
            ThrowIfDisposed(state);
            previous = state.Count;
            if ((long)state.Count + releaseCount > state.MaxCount)
            {
                throw new SemaphoreFullException();
            }

            state.Count += releaseCount;

            // Serve waiters in arrival order. Resolving/removing each waiter and consuming its permit
            // happen atomically under the resource gate; callbacks and task continuations run afterward.
            while (state.Waiters.Count > 0 && state.Count > 0)
            {
                var waiter = state.Waiters[0];
                if (TryResolveUnderLock(
                    state,
                    waiter,
                    WaiterOutcome.Signaled,
                    CancellationToken.None,
                    out var cleanup))
                {
                    completed.Add(cleanup);
                    state.Count--;
                }
            }
        }

        CompleteWaiters(completed);
        RaceSynchronization.Signal(instance);
        return previous;
    }

    /// <summary>Controlled <see cref="SemaphoreSlim.Dispose()"/>.</summary>
    /// <param name="instance">The receiving semaphore.</param>
    public static void Dispose(SemaphoreSlim instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.Dispose");
        ArgumentNullException.ThrowIfNull(instance);
        var state = StateOf(instance, "System.Threading.SemaphoreSlim.Dispose");
        List<WaiterCleanup> completed = [];
        WaitHandle? availableHandle;
        lock (state.Gate)
        {
            if (state.Disposed)
            {
                return;
            }

            state.Disposed = true;
            availableHandle = state.AvailableHandle;
            foreach (var waiter in state.Waiters.ToArray())
            {
                if (TryResolveUnderLock(
                    state,
                    waiter,
                    WaiterOutcome.Disposed,
                    CancellationToken.None,
                    out var cleanup))
                {
                    completed.Add(cleanup);
                }
            }
        }

        // Cleanup can run callbacks or acquire other resource gates, so none of it runs under State.Gate.
        CompleteWaiters(completed);
        ControlledWaitHandle.DisposeBridge(availableHandle);
    }

    /// <summary>
    /// Controlled <see cref="SemaphoreSlim.AvailableWaitHandle"/>. Returns a controlled manual-reset wait
    /// handle - materialised once and cached - whose signalled state tracks whether a permit is available
    /// (count &gt; 0). Waiting on it observes availability without consuming a permit, exactly as the BCL
    /// handle does; it composes with <c>WaitAny</c>/<c>WaitAll</c> across the controlled surface.
    /// </summary>
    /// <param name="instance">The receiving semaphore.</param>
    /// <returns>The controlled availability handle.</returns>
    public static WaitHandle AvailableWaitHandle(SemaphoreSlim instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.SemaphoreSlim.get_AvailableWaitHandle");
        ArgumentNullException.ThrowIfNull(instance);
        var state = StateOf(instance, "System.Threading.SemaphoreSlim.get_AvailableWaitHandle");
        lock (state.Gate)
        {
            ThrowIfDisposed(state);
            return state.AvailableHandle ??= ControlledWaitHandle.CreateBridge(state.Count > 0);
        }
    }

    private static bool WaitControlled(SemaphoreSlim instance, int millisecondsTimeout, CancellationToken cancellationToken)
    {
        ValidateTimeout(millisecondsTimeout);
        var state = StateOf(instance, "System.Threading.SemaphoreSlim.Wait");
        Waiter waiter = null!;
        bool acquiredImmediately = false;
        lock (state.Gate)
        {
            ThrowIfDisposed(state);
            cancellationToken.ThrowIfCancellationRequested();

            if (state.Count > 0)
            {
                state.Count--;
                acquiredImmediately = true;
            }
            else if (millisecondsTimeout == 0)
            {
                return false;
            }
            else
            {
                waiter = EnqueueUnderLock(state, millisecondsTimeout);
            }
        }

        if (acquiredImmediately)
        {
            RaceSynchronization.Wait(instance);
            return true;
        }

        AttachCancellation(state, waiter, cancellationToken);
        SimulationTaskRuntime.DrainUntil(() => waiter.Completion.Task.IsCompleted, WaitApi);

        // The waiter completes as served (true), timed-out (false), or cancelled. GetResult rethrows the
        // OperationCanceledException for a cancelled waiter, so synchronous cancellation observes the same
        // exception as the real SemaphoreSlim; a timeout returns false.
        bool acquired = waiter.Completion.Task.GetAwaiter().GetResult();
        if (acquired)
        {
            RaceSynchronization.Wait(instance);
        }

        return acquired;
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

        var state = StateOf(instance, "System.Threading.SemaphoreSlim.WaitAsync");
        Waiter waiter;
        lock (state.Gate)
        {
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
                RaceSynchronization.Wait(instance);
                Task<bool> completed = Task.FromResult(true);
                RaceSynchronization.Signal(completed);
                return completed;
            }

            if (millisecondsTimeout == 0)
            {
                return Task.FromResult(false);
            }

            waiter = EnqueueUnderLock(state, millisecondsTimeout);
        }

        AttachCancellation(state, waiter, cancellationToken);
        return waiter.Completion.Task;
    }

    private static Waiter EnqueueUnderLock(State state, int millisecondsTimeout)
    {
        var waiter = new Waiter();
        state.Waiters.Add(waiter);

        if (millisecondsTimeout != Timeout.Infinite)
        {
            // A finite wait registers a deterministic virtual-time deadline. It elapses only when the loop
            // has no other runnable work and advances modelled time to it, so a permit that could be served
            // now (Release) or a cancellation possible now always wins over the timeout - the first-winner
            // policy - and a timeout completes the waiter with false.
            waiter.Deadline = SimulationTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout),
                onElapsed: () => ResolveTimedOut(state, waiter),
                WaitApi);
        }

        return waiter;
    }

    private static void AttachCancellation(State state, Waiter waiter, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        BeforeCancellationRegistrationForTesting?.Invoke();
        var registration = cancellationToken.Register(
            static callbackState =>
            {
                var (semaphoreState, canceledWaiter, token) =
                    ((State, Waiter, CancellationToken))callbackState!;
                ResolveCanceled(semaphoreState, canceledWaiter, token);
            },
            (state, waiter, cancellationToken));

        CancellationTokenRegistration detachedRegistration = default;
        lock (state.Gate)
        {
            if (waiter.Outcome == WaiterOutcome.Pending && state.Waiters.Contains(waiter))
            {
                waiter.Registration = registration;
            }
            else
            {
                detachedRegistration = registration;
            }
        }

        detachedRegistration.Dispose();
    }

    private static void ResolveCanceled(State state, Waiter waiter, CancellationToken cancellationToken)
    {
        WaiterCleanup cleanup;
        lock (state.Gate)
        {
            if (!TryResolveUnderLock(
                state,
                waiter,
                WaiterOutcome.Canceled,
                cancellationToken,
                out cleanup))
            {
                return;
            }
        }

        CompleteWaiter(cleanup);
    }

    private static void ResolveTimedOut(State state, Waiter waiter)
    {
        WaiterCleanup cleanup;
        lock (state.Gate)
        {
            if (!TryResolveUnderLock(
                state,
                waiter,
                WaiterOutcome.TimedOut,
                CancellationToken.None,
                out cleanup,
                cancelDeadline: false))
            {
                return;
            }
        }

        CompleteWaiter(cleanup);
    }

    private static bool TryResolveUnderLock(
        State state,
        Waiter waiter,
        WaiterOutcome outcome,
        CancellationToken cancellationToken,
        out WaiterCleanup cleanup,
        bool cancelDeadline = true)
    {
        if (waiter.Outcome != WaiterOutcome.Pending || !state.Waiters.Remove(waiter))
        {
            cleanup = default;
            return false;
        }

        waiter.Outcome = outcome;
        var registration = waiter.Registration;
        waiter.Registration = default;
        var deadline = waiter.Deadline;
        waiter.Deadline = null;
        cleanup = new WaiterCleanup(
            waiter,
            outcome,
            registration,
            cancelDeadline ? deadline : null,
            cancellationToken);
        return true;
    }

    private static void CompleteWaiters(List<WaiterCleanup> completed)
    {
        foreach (var cleanup in completed)
        {
            CompleteWaiter(cleanup);
        }
    }

    private static void CompleteWaiter(WaiterCleanup cleanup)
    {
        cleanup.Deadline?.Cancel();
        cleanup.Registration.Dispose();
        switch (cleanup.Outcome)
        {
            case WaiterOutcome.Signaled:
                cleanup.Waiter.Completion.TrySetResult(true);
                break;
            case WaiterOutcome.TimedOut:
                cleanup.Waiter.Completion.TrySetResult(false);
                break;
            case WaiterOutcome.Canceled:
                cleanup.Waiter.Completion.TrySetCanceled(cleanup.CancellationToken);
                break;
            case WaiterOutcome.Disposed:
                cleanup.Waiter.Completion.TrySetException(new ObjectDisposedException(nameof(SemaphoreSlim)));
                break;
        }

        RaceSynchronization.Signal(cleanup.Waiter.Completion.Task);
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
