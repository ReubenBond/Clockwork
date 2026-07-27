using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
/// surfaces as the standard controlled deadlock diagnostic. Outside a simulation every shim delegates to
/// the real <see cref="WaitHandle"/>.
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
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitOne();
        }

        return WaitControlled(instance, Timeout.Infinite, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(int)"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, int millisecondsTimeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitOne(millisecondsTimeout);
        }

        return WaitControlled(instance, millisecondsTimeout, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(TimeSpan)"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitOne(timeout);
        }

        return WaitControlled(instance, ToMilliseconds(timeout), WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(int, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="millisecondsTimeout">The virtual-time timeout, <see cref="Timeout.Infinite"/>, or zero.</param>
    /// <param name="exitContext">Ignored inside a simulation (there is no synchronization context to exit).</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, int millisecondsTimeout, bool exitContext)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitOne(millisecondsTimeout, exitContext);
        }

        return WaitControlled(instance, millisecondsTimeout, WaitOneApi);
    }

    /// <summary>Controlled <see cref="WaitHandle.WaitOne(TimeSpan, bool)"/>. The <paramref name="exitContext"/> flag has no meaning to the cooperative scheduler.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="timeout">The virtual-time timeout.</param>
    /// <param name="exitContext">Ignored inside a simulation (there is no synchronization context to exit).</param>
    /// <returns><see langword="true"/> when the handle is signalled before the deadline.</returns>
    public static bool WaitOne(WaitHandle instance, TimeSpan timeout, bool exitContext)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.WaitOne(timeout, exitContext);
        }

        return WaitControlled(instance, ToMilliseconds(timeout), WaitOneApi);
    }

    // ---- lifecycle ----

    /// <summary>Controlled <see cref="WaitHandle.Dispose()"/>.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    public static void Dispose(WaitHandle instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (States.TryGetValue(instance, out EventState? state))
        {
            state.Disposed = true;
        }

        // Always release the real identity object's OS handle; the modelled state above is what a
        // controlled wait observes. Safe both inside and outside a simulation.
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
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
#pragma warning disable CS0618 // WaitHandle.Handle is deprecated; the passthrough must still honour it outside a simulation.
            return instance.Handle;
#pragma warning restore CS0618
        }

        throw RawHandleRejected(HandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.Handle"/> setter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="value">The requested native handle.</param>
    public static void SetHandle(WaitHandle instance, IntPtr value)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
#pragma warning disable CS0618 // WaitHandle.Handle is deprecated; the passthrough must still honour it outside a simulation.
            instance.Handle = value;
#pragma warning restore CS0618
            return;
        }

        throw RawHandleRejected(HandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.SafeWaitHandle"/> getter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <returns>Never returns inside a simulation; throws instead.</returns>
    public static SafeWaitHandle GetSafeWaitHandle(WaitHandle instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return instance.SafeWaitHandle;
        }

        throw RawHandleRejected(SafeWaitHandleApi);
    }

    /// <summary>Rejected controlled <see cref="WaitHandle.SafeWaitHandle"/> setter.</summary>
    /// <param name="instance">The receiving wait handle.</param>
    /// <param name="value">The requested safe handle.</param>
    public static void SetSafeWaitHandle(WaitHandle instance, SafeWaitHandle value)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            instance.SafeWaitHandle = value;
            return;
        }

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
