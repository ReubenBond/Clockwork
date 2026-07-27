using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="WaitHandle"/> base surface (the members every event/wait handle
/// inherits): the <see cref="WaitHandle.WaitOne()"/> overloads, <see cref="WaitHandle.Dispose()"/> /
/// <c>Close</c>, and the raw OS-handle accessors. <see cref="WaitHandle"/> is abstract, so - as with the
/// controlled <see cref="SemaphoreSlim"/> surface - the controlled object <em>is</em> a real
/// <see cref="AutoResetEvent"/> / <see cref="ManualResetEvent"/> / <see cref="EventWaitHandle"/> used
/// purely as an identity handle, and the rewriter redirects each inherited instance member to a static
/// method here whose first parameter is the receiver.
/// </para>
/// <para>
/// This type also owns the shared, weak-keyed signalled-state registry that every controlled event uses
/// (<see cref="ControlledEventWaitHandle"/> populates it in its <c>Create</c> factories, and Phase 7B's
/// <see cref="SemaphoreSlim.AvailableWaitHandle"/> bridge registers a manual-reset entry). A synchronous
/// <see cref="WaitHandle.WaitOne()"/> pumps the deterministic loop until the handle is signalled (an
/// auto-reset handle consumes the signal, a manual-reset handle leaves it set), or a finite virtual-time
/// deadline elapses - never real time, never a blocked physical thread. A wait that can never be satisfied
/// surfaces as the standard controlled deadlock diagnostic.
/// </para>
/// <para>
/// Adapted from Microsoft Coyote (MIT), whose controlled wait handles likewise model the signalled state
/// on the cooperative scheduler instead of touching a kernel object. The raw handle accessors
/// (<c>Handle</c>/<c>SafeWaitHandle</c>) expose an OS primitive and are rejected precisely, as is a wait
/// against a handle the controlled surface never created.
/// </para>
/// </summary>
public static class ControlledWaitHandle
{
    private const string WaitOneApi = "System.Threading.WaitHandle.WaitOne";
    private const string HandleApi = "System.Threading.WaitHandle.Handle";
    private const string SafeWaitHandleApi = "System.Threading.WaitHandle.SafeWaitHandle";

    /// <summary>A single blocked <see cref="WaitHandle.WaitOne()"/> caller.</summary>
    internal sealed class Waiter
    {
        public readonly TaskCompletionSource<bool> Completion = new();

        // The virtual-time deadline for a finite wait, or null for an infinite wait. Cancelled when the
        // waiter completes for any other reason so a stale timeout cannot fire.
        public IControlledTimeout? Deadline;
    }

    /// <summary>The modelled signalled state of one controlled event/wait handle.</summary>
    internal sealed class EventState
    {
        public EventState(EventResetMode mode, bool signaled)
        {
            Mode = mode;
            Signaled = signaled;
        }

        public EventResetMode Mode { get; }

        public bool Signaled { get; set; }

        public bool Disposed { get; set; }

        // Callers blocked in WaitOne, in arrival order. A signal serves the front waiter(s).
        public List<Waiter> Waiters { get; } = new();
    }

    private static readonly ConditionalWeakTable<WaitHandle, EventState> States = new();

    /// <summary>Associates a modelled signalled state with a controlled wait handle. Used by the event and bridge factories.</summary>
    /// <param name="handle">The real wait-handle identity object.</param>
    /// <param name="state">The modelled state to associate.</param>
    internal static void Register(WaitHandle handle, EventState state) => States.AddOrUpdate(handle, state);

    /// <summary>Attempts to resolve the modelled state of a wait handle.</summary>
    internal static bool TryGetState(WaitHandle handle, out EventState state) => States.TryGetValue(handle, out state!);

    private static EventState StateOrThrow(WaitHandle handle, string api)
    {
        if (States.TryGetValue(handle, out EventState? state))
        {
            return state;
        }

        throw new ControlledWaitHandleUnsupportedException(
            api,
            "the wait handle was not created by Clockwork's controlled event surface, so it has no modelled " +
            "signalled state; waiting on an uncontrolled kernel handle would block a physical thread outside " +
            "the deterministic scheduler.");
    }

    /// <summary>Resolves the modelled state of a controlled event, throwing if unknown or disposed. Used by Set/Reset.</summary>
    internal static EventState StateForOperation(WaitHandle handle, string api)
    {
        EventState state = StateOrThrow(handle, api);
        ThrowIfDisposed(state);
        return state;
    }

    // ---- externally-driven bridge handle (SemaphoreSlim.AvailableWaitHandle) ----

    /// <summary>
    /// Creates a controlled manual-reset wait handle whose signalled state is driven by an external
    /// condition rather than by <c>Set</c>/<c>Reset</c>. The real <see cref="ManualResetEvent"/> is used
    /// purely as an identity handle; callers publish state transitions through
    /// <see cref="UpdateBridgeSignal"/>. This backs <see cref="SemaphoreSlim.AvailableWaitHandle"/>, whose
    /// handle is signalled exactly while the semaphore has a permit available (count &gt; 0).
    /// </summary>
    /// <param name="signaled">The initial signalled state (permit available).</param>
    /// <returns>The identity handle registered with a manual-reset modelled state.</returns>
    internal static ManualResetEvent CreateBridge(bool signaled)
    {
        var handle = new ManualResetEvent(signaled);
        Register(handle, new EventState(EventResetMode.ManualReset, signaled));
        return handle;
    }

    /// <summary>
    /// Publishes the signalled state of a bridge handle to track its external condition. A rising edge
    /// releases every waiter (manual-reset semantics); waiting on the handle never consumes the underlying
    /// resource. No-ops if the handle is unknown or already disposed.
    /// </summary>
    /// <param name="handle">The bridge identity handle.</param>
    /// <param name="signaled">The new signalled state.</param>
    internal static void UpdateBridgeSignal(WaitHandle handle, bool signaled)
    {
        if (!TryGetState(handle, out EventState state) || state.Disposed)
        {
            return;
        }

        state.Signaled = signaled;
        if (signaled)
        {
            ReleaseWaiters(state);
        }
    }

    /// <summary>Marks a bridge handle's modelled state disposed so subsequent waits fault precisely.</summary>
    /// <param name="handle">The bridge identity handle, or <see langword="null"/> if none was materialised.</param>
    internal static void DisposeBridge(WaitHandle? handle)
    {
        if (handle is not null && TryGetState(handle, out EventState state))
        {
            state.Disposed = true;
        }
    }

    // ---- signalled-state kernel shared by events and WaitOne ----

    /// <summary>
    /// Consumes a signal if the handle is set: an auto-reset handle clears its signal (one waiter's worth),
    /// a manual-reset handle stays set. Returns whether the caller may proceed without blocking.
    /// </summary>
    internal static bool TryConsume(EventState state)
    {
        if (!state.Signaled)
        {
            return false;
        }

        if (state.Mode == EventResetMode.AutoReset)
        {
            state.Signaled = false;
        }

        return true;
    }

    /// <summary>
    /// Releases waiters made runnable by the current signalled state. A manual-reset signal releases every
    /// waiter and stays set; an auto-reset signal releases exactly one waiter and then clears (that waiter
    /// consumed the signal).
    /// </summary>
    internal static void ReleaseWaiters(EventState state)
    {
        if (state.Mode == EventResetMode.ManualReset)
        {
            if (!state.Signaled)
            {
                return;
            }

            for (int i = 0; i < state.Waiters.Count; i++)
            {
                Complete(state.Waiters[i]);
            }

            state.Waiters.Clear();
            return;
        }

        // Auto-reset: a single set releases exactly one eligible waiter and is consumed by it.
        while (state.Signaled && state.Waiters.Count > 0)
        {
            Waiter waiter = state.Waiters[0];
            state.Waiters.RemoveAt(0);
            state.Signaled = false;
            Complete(waiter);
        }
    }

    private static void Complete(Waiter waiter)
    {
        waiter.Deadline?.Cancel();
        waiter.Completion.TrySetResult(true);
    }

    internal static bool WaitControlled(WaitHandle handle, int millisecondsTimeout, string api)
    {
        ValidateTimeout(millisecondsTimeout);
        EventState state = StateOrThrow(handle, api);
        ThrowIfDisposed(state);

        if (TryConsume(state))
        {
            return true;
        }

        if (millisecondsTimeout == 0)
        {
            return false;
        }

        var waiter = new Waiter();
        state.Waiters.Add(waiter);
        if (millisecondsTimeout != Timeout.Infinite)
        {
            waiter.Deadline = ControlledTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout),
                onElapsed: () =>
                {
                    if (state.Waiters.Remove(waiter))
                    {
                        waiter.Completion.TrySetResult(false);
                    }
                },
                api);
        }

        ControlledTaskRuntime.DrainUntil(() => waiter.Completion.Task.IsCompleted, api);
        return waiter.Completion.Task.GetAwaiter().GetResult();
    }

    // ---- WaitOne overloads (receiver-first) ----

    /// <summary>Controlled <see cref="WaitHandle.WaitOne()"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <returns><see langword="true"/> when the handle is signalled.</returns>
    public static bool WaitOne(WaitHandle instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitOneApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, Timeout.Infinite, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(int)"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitOneApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, millisecondsTimeout, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitOneApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, ToMilliseconds(timeout), WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(int, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <param name="exitContext">Ignored inside a simulation (there is no synchronization context to exit).</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, int millisecondsTimeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitOneApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, millisecondsTimeout, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(TimeSpan, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <param name="exitContext">Ignored inside a simulation (there is no synchronization context to exit).</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, TimeSpan timeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitOneApi);
        ArgumentNullException.ThrowIfNull(instance);
        return WaitControlled(instance, ToMilliseconds(timeout), WaitOneApi);
    }

    // ---- WaitAny / WaitAll / SignalAndWait (static multi-handle operations) ----

    /// <summary>The value returned by <see cref="WaitHandle.WaitAny(WaitHandle[])"/> when the wait times out.</summary>
    public const int WaitTimeout = 258;

    /// <summary>Controlled <see cref="WaitHandle.WaitAny(WaitHandle[])"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <returns>The array index of the handle that satisfied the wait.</returns>
    public static int WaitAny(WaitHandle[] waitHandles)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAnyApi);
        return WaitAnyControlled(waitHandles, Timeout.Infinite);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAny(WaitHandle[], int)"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <returns>The signalled handle's index, or <see cref="WaitTimeout"/> on timeout.</returns>
    public static int WaitAny(WaitHandle[] waitHandles, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAnyApi);
        return WaitAnyControlled(waitHandles, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAny(WaitHandle[], TimeSpan)"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <returns>The signalled handle's index, or <see cref="WaitTimeout"/> on timeout.</returns>
    public static int WaitAny(WaitHandle[] waitHandles, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAnyApi);
        return WaitAnyControlled(waitHandles, ToMilliseconds(timeout));
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAny(WaitHandle[], int, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns>The signalled handle's index, or <see cref="WaitTimeout"/> on timeout.</returns>
    public static int WaitAny(WaitHandle[] waitHandles, int millisecondsTimeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAnyApi);
        return WaitAnyControlled(waitHandles, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAny(WaitHandle[], TimeSpan, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns>The signalled handle's index, or <see cref="WaitTimeout"/> on timeout.</returns>
    public static int WaitAny(WaitHandle[] waitHandles, TimeSpan timeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAnyApi);
        return WaitAnyControlled(waitHandles, ToMilliseconds(timeout));
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAll(WaitHandle[])"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <returns><see langword="true"/> when every handle became signalled.</returns>
    public static bool WaitAll(WaitHandle[] waitHandles)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAllApi);
        return WaitAllControlled(waitHandles, Timeout.Infinite);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAll(WaitHandle[], int)"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <returns><see langword="true"/> when every handle became signalled before the deadline.</returns>
    public static bool WaitAll(WaitHandle[] waitHandles, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAllApi);
        return WaitAllControlled(waitHandles, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAll(WaitHandle[], TimeSpan)"/>.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <returns><see langword="true"/> when every handle became signalled before the deadline.</returns>
    public static bool WaitAll(WaitHandle[] waitHandles, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAllApi);
        return WaitAllControlled(waitHandles, ToMilliseconds(timeout));
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAll(WaitHandle[], int, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns><see langword="true"/> when every handle became signalled before the deadline.</returns>
    public static bool WaitAll(WaitHandle[] waitHandles, int millisecondsTimeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAllApi);
        return WaitAllControlled(waitHandles, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitAll(WaitHandle[], TimeSpan, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="waitHandles">The handles to wait on.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns><see langword="true"/> when every handle became signalled before the deadline.</returns>
    public static bool WaitAll(WaitHandle[] waitHandles, TimeSpan timeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitAllApi);
        return WaitAllControlled(waitHandles, ToMilliseconds(timeout));
    }

    /// <summary>Controlled <see cref="WaitHandle.SignalAndWait(WaitHandle, WaitHandle)"/>.</summary>
    /// <param name="toSignal">The handle to signal.</param>
    /// <param name="toWaitOn">The handle to then wait on.</param>
    /// <returns><see langword="true"/> when <paramref name="toWaitOn"/> became signalled.</returns>
    public static bool SignalAndWait(WaitHandle toSignal, WaitHandle toWaitOn)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        return SignalAndWaitControlled(toSignal, toWaitOn, Timeout.Infinite);
    }

    /// <summary>Controlled <see cref="WaitHandle.SignalAndWait(WaitHandle, WaitHandle, int, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="toSignal">The handle to signal.</param>
    /// <param name="toWaitOn">The handle to then wait on.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns><see langword="true"/> when <paramref name="toWaitOn"/> became signalled before the deadline.</returns>
    public static bool SignalAndWait(WaitHandle toSignal, WaitHandle toWaitOn, int millisecondsTimeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        return SignalAndWaitControlled(toSignal, toWaitOn, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="WaitHandle.SignalAndWait(WaitHandle, WaitHandle, TimeSpan, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="toSignal">The handle to signal.</param>
    /// <param name="toWaitOn">The handle to then wait on.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <param name="exitContext">Ignored inside a simulation.</param>
    /// <returns><see langword="true"/> when <paramref name="toWaitOn"/> became signalled before the deadline.</returns>
    public static bool SignalAndWait(WaitHandle toSignal, WaitHandle toWaitOn, TimeSpan timeout, bool exitContext)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        return SignalAndWaitControlled(toSignal, toWaitOn, ToMilliseconds(timeout));
    }

    // ---- multi-handle kernel ----

    // The largest handle array WaitAny/WaitAll accept, matching the BCL's WAIT_OBJECT limit.
    private const int MaxWaitHandles = 64;

    private const string WaitAnyApi = "System.Threading.WaitHandle.WaitAny";
    private const string WaitAllApi = "System.Threading.WaitHandle.WaitAll";
    private const string SignalAndWaitApi = "System.Threading.WaitHandle.SignalAndWait";

    private static int WaitAnyControlled(WaitHandle[] waitHandles, int millisecondsTimeout)
    {
        ValidateTimeout(millisecondsTimeout);
        EventState[] states = ResolveStates(waitHandles, WaitAnyApi, requireUnique: false);

        // Fast path: serve the lowest-index already-signalled handle, consuming it (auto-reset clears).
        int index = FirstSignaled(states);
        if (index >= 0)
        {
            TryConsume(states[index]);
            return index;
        }

        if (millisecondsTimeout == 0)
        {
            return WaitTimeout;
        }

        bool timedOut = false;
        IControlledTimeout? deadline = millisecondsTimeout == Timeout.Infinite
            ? null
            : ControlledTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout), onElapsed: () => timedOut = true, WaitAnyApi);

        ControlledTaskRuntime.DrainUntil(() => timedOut || FirstSignaled(states) >= 0, WaitAnyApi);

        index = FirstSignaled(states);
        if (index >= 0)
        {
            deadline?.Cancel();
            TryConsume(states[index]);
            return index;
        }

        return WaitTimeout;
    }

    private static bool WaitAllControlled(WaitHandle[] waitHandles, int millisecondsTimeout)
    {
        ValidateTimeout(millisecondsTimeout);
        EventState[] states = ResolveStates(waitHandles, WaitAllApi, requireUnique: true);

        // Fast path: only succeeds if every handle is simultaneously signalled, and then consumes them all
        // atomically (an auto-reset handle is never partially consumed).
        if (AllSignaled(states))
        {
            ConsumeAll(states);
            return true;
        }

        if (millisecondsTimeout == 0)
        {
            return false;
        }

        bool timedOut = false;
        IControlledTimeout? deadline = millisecondsTimeout == Timeout.Infinite
            ? null
            : ControlledTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout), onElapsed: () => timedOut = true, WaitAllApi);

        ControlledTaskRuntime.DrainUntil(() => timedOut || AllSignaled(states), WaitAllApi);

        if (AllSignaled(states))
        {
            deadline?.Cancel();
            ConsumeAll(states);
            return true;
        }

        return false;
    }

    private static bool SignalAndWaitControlled(WaitHandle toSignal, WaitHandle toWaitOn, int millisecondsTimeout)
    {
        ArgumentNullException.ThrowIfNull(toSignal);
        ArgumentNullException.ThrowIfNull(toWaitOn);

        // The signal handle must be a controlled event (only events are settable in Phase 7B); a Mutex /
        // Semaphore release is Phase 8 and would reject here via the missing modelled state.
        EventState signalState = StateForOperation(toSignal, SignalAndWaitApi);
        signalState.Signaled = true;
        ReleaseWaiters(signalState);

        // Then block on the second handle, exactly as a controlled WaitOne would.
        return WaitControlled(toWaitOn, millisecondsTimeout, SignalAndWaitApi);
    }

    private static EventState[] ResolveStates(WaitHandle[] waitHandles, string api, bool requireUnique)
    {
        ArgumentNullException.ThrowIfNull(waitHandles);
        if (waitHandles.Length == 0)
        {
            throw new ArgumentException("Waithandles cannot be empty.", nameof(waitHandles));
        }

        if (waitHandles.Length > MaxWaitHandles)
        {
            throw new NotSupportedException($"The number of WaitHandles must be less than or equal to {MaxWaitHandles}.");
        }

        var states = new EventState[waitHandles.Length];
        for (int i = 0; i < waitHandles.Length; i++)
        {
            WaitHandle handle = waitHandles[i];
            if (handle is null)
            {
                throw new ArgumentNullException(nameof(waitHandles), "A wait handle in the array was null.");
            }

            if (requireUnique)
            {
                for (int j = 0; j < i; j++)
                {
                    if (ReferenceEquals(waitHandles[j], handle))
                    {
                        throw new DuplicateWaitObjectException(nameof(waitHandles), "The wait-handle array contains a duplicate handle.");
                    }
                }
            }

            EventState state = StateOrThrow(handle, api);
            ThrowIfDisposed(state);
            states[i] = state;
        }

        return states;
    }

    private static int FirstSignaled(EventState[] states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i].Signaled)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool AllSignaled(EventState[] states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            if (!states[i].Signaled)
            {
                return false;
            }
        }

        return true;
    }

    private static void ConsumeAll(EventState[] states)
    {
        for (int i = 0; i < states.Length; i++)
        {
            TryConsume(states[i]);
        }
    }

    // ---- registered-wait kernel (ThreadPool.RegisterWaitForSingleObject) ----

    /// <summary>
    /// Arms one passive, non-blocking registered-wait iteration: adds a waiter to the target event's FIFO
    /// waiter set (so an auto-reset signal is consumed exactly once, in arrival order, by
    /// <see cref="ReleaseWaiters"/>) and registers a virtual-time deadline that completes the waiter with
    /// <see langword="false"/> on elapse. The caller schedules a controlled continuation on the returned
    /// waiter's completion task rather than blocking the logical thread, so a background registration never
    /// occupies the cooperative scheduler. A result of <see langword="true"/> means signalled;
    /// <see langword="false"/> means the deadline elapsed (or the waiter was cancelled by
    /// <see cref="CancelRegisteredWaiter"/>).
    /// </summary>
    /// <param name="state">The controlled target event's modelled state.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, or <see cref="Timeout.Infinite"/>.</param>
    /// <param name="api">The originating API name, used for diagnostics.</param>
    /// <returns>The registered waiter, whose completion task the caller continues on.</returns>
    internal static Waiter ArmRegisteredWaiter(EventState state, int millisecondsTimeout, string api)
    {
        var waiter = new Waiter();
        state.Waiters.Add(waiter);
        if (millisecondsTimeout != Timeout.Infinite)
        {
            waiter.Deadline = ControlledTaskRuntime.RegisterTimeout(
                TimeSpan.FromMilliseconds(millisecondsTimeout),
                onElapsed: () =>
                {
                    if (state.Waiters.Remove(waiter))
                    {
                        waiter.Completion.TrySetResult(false);
                    }
                },
                api);
        }

        return waiter;
    }

    /// <summary>
    /// Cancels a still-pending registered waiter (used by <c>Unregister</c>): removes it from the event's
    /// waiter set, cancels its virtual-time deadline, and completes it with <see langword="false"/> so the
    /// caller's scheduled continuation runs and observes the cancellation. Idempotent if already completed.
    /// </summary>
    /// <param name="state">The event the waiter is registered on.</param>
    /// <param name="waiter">The pending waiter to cancel.</param>
    internal static void CancelRegisteredWaiter(EventState state, Waiter waiter)
    {
        state.Waiters.Remove(waiter);
        waiter.Deadline?.Cancel();
        waiter.Completion.TrySetResult(false);
    }

    /// <summary>
    /// Sets a controlled event's modelled signal and releases eligible waiters, if the handle has modelled
    /// state and is not disposed. Used to signal an optional <c>Unregister</c> completion handle. No-ops for
    /// an uncontrolled or disposed handle.
    /// </summary>
    /// <param name="handle">The handle to signal, or <see langword="null"/>.</param>
    internal static void TrySignal(WaitHandle? handle)
    {
        if (handle is not null && TryGetState(handle, out EventState state) && !state.Disposed)
        {
            state.Signaled = true;
            ReleaseWaiters(state);
        }
    }

    // ---- lifecycle ----

    /// <summary>Controlled <see cref="WaitHandle.Dispose()"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    public static void Dispose(WaitHandle instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.WaitHandle.Dispose");
        ArgumentNullException.ThrowIfNull(instance);
        if (States.TryGetValue(instance, out EventState? state))
        {
            state.Disposed = true;
        }

        // Always release the real identity object's OS handle; the modelled state above is what a
        // controlled wait observes.
        instance.Dispose();
    }

    /// <summary>Controlled <see cref="WaitHandle.Close()"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    public static void Close(WaitHandle instance) => Dispose(instance);

    // ---- raw OS-handle accessors: rejected precisely ----

    /// <summary>Rejected controlled <see cref="WaitHandle.Handle"/> getter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <returns>Never returns inside a simulation; throws instead.</returns>
    public static IntPtr GetHandle(WaitHandle instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(HandleApi);
        ArgumentNullException.ThrowIfNull(instance);
        throw RawHandleRejected(HandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.Handle"/> setter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="value">The requested native handle.</param>
    public static void SetHandle(WaitHandle instance, IntPtr value)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(HandleApi);
        ArgumentNullException.ThrowIfNull(instance);
        throw RawHandleRejected(HandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.SafeWaitHandle"/> getter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <returns>Never returns inside a simulation; throws instead.</returns>
    public static SafeWaitHandle GetSafeWaitHandle(WaitHandle instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SafeWaitHandleApi);
        ArgumentNullException.ThrowIfNull(instance);
        throw RawHandleRejected(SafeWaitHandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.SafeWaitHandle"/> setter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="value">The requested safe handle.</param>
    public static void SetSafeWaitHandle(WaitHandle instance, SafeWaitHandle value)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SafeWaitHandleApi);
        ArgumentNullException.ThrowIfNull(instance);
        throw RawHandleRejected(SafeWaitHandleApi);
    }

    private static ControlledWaitHandleUnsupportedException RawHandleRejected(string api) =>
        new(api,
            "it exposes the underlying OS wait handle, which would let code block a physical thread or " +
            "signal a kernel object outside the deterministic scheduler.");

    private static void ThrowIfDisposed(EventState state) =>
        ObjectDisposedException.ThrowIf(state.Disposed, typeof(WaitHandle));

    private static void ValidateTimeout(int millisecondsTimeout)
    {
        if (millisecondsTimeout < Timeout.Infinite)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout), millisecondsTimeout, "The timeout must be -1 (infinite) or a non-negative value.");
        }
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        long total = (long)timeout.TotalMilliseconds;
        if (total < Timeout.Infinite || total > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The timeout must be between -1 and Int32.MaxValue milliseconds.");
        }

        return (int)total;
    }
}
