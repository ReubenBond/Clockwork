using System.Threading.Tasks;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>A fully controlled replacement for <see cref="System.Threading.CountdownEvent"/>.</summary>
public sealed class ControlledCountdownEvent : IDisposable
{
    private const string TypeName = "System.Threading.CountdownEvent";
    private const string WaitApi = TypeName + ".Wait";

    private sealed class Waiter
    {
        public TaskCompletionSource<bool> Completion { get; } = new();

        public CancellationTokenRegistration Registration;

        public ISimulationTimer? Deadline;

        public bool Pending { get; set; } = true;
    }

    private readonly object _gate = new();
    private readonly List<Waiter> _waiters = [];
    private bool _disposed;
    private int _currentCount;
    private int _initialCount;
    private WaitHandle? _waitHandle;

    /// <summary>Initializes a controlled countdown event.</summary>
    public ControlledCountdownEvent(int initialCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + "..ctor");
        ValidateCount(initialCount, nameof(initialCount));
        _initialCount = initialCount;
        _currentCount = initialCount;
    }

    /// <summary>Gets the current count.</summary>
    public int CurrentCount
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_CurrentCount");
            lock (_gate)
            {
                return _currentCount;
            }
        }
    }

    /// <summary>Gets the configured initial count.</summary>
    public int InitialCount
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_InitialCount");
            lock (_gate)
            {
                return _initialCount;
            }
        }
    }

    /// <summary>Gets whether the count has reached zero.</summary>
    public bool IsSet
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_IsSet");
            lock (_gate)
            {
                return _currentCount == 0;
            }
        }
    }

    /// <summary>Gets the cached controlled manual-reset bridge for the set state.</summary>
    public WaitHandle WaitHandle
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_WaitHandle");
            lock (_gate)
            {
                ThrowIfDisposed();
                return _waitHandle ??= ControlledWaitHandle.CreateBridge(_currentCount == 0);
            }
        }
    }

    /// <summary>Adds one to the count.</summary>
    public void AddCount() => AddCount(1);

    /// <summary>Adds to the count.</summary>
    public void AddCount(int signalCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".AddCount");
        AddCountCore(signalCount, throwIfSet: true);
    }

    /// <summary>Attempts to add one to the count.</summary>
    public bool TryAddCount() => TryAddCount(1);

    /// <summary>Attempts to add to the count.</summary>
    public bool TryAddCount(int signalCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".TryAddCount");
        return AddCountCore(signalCount, throwIfSet: false);
    }

    /// <summary>Signals one count and returns whether the event became set.</summary>
    public bool Signal() => Signal(1);

    /// <summary>Signals counts and returns whether the event became set.</summary>
    public bool Signal(int signalCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".Signal");
        ArgumentOutOfRangeException.ThrowIfLessThan(signalCount, 1);

        List<Waiter>? waiters = null;
        WaitHandle? bridge = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (signalCount > _currentCount)
            {
                throw new InvalidOperationException("The signal count exceeds the current count.");
            }

            _currentCount -= signalCount;
            if (_currentCount == 0)
            {
                waiters = TakeWaitersUnderLock();
                bridge = _waitHandle;
            }
        }

        if (bridge is not null)
        {
            ControlledWaitHandle.UpdateBridgeSignal(bridge, signaled: true);
        }

        CompleteWaiters(waiters, exception: null);
        return waiters is not null;
    }

    /// <summary>Restores the current count to the configured initial count.</summary>
    public void Reset() => Reset(_initialCount);

    /// <summary>Changes the initial count and restores the current count.</summary>
    public void Reset(int count)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".Reset");
        ValidateCount(count, nameof(count));
        WaitHandle? bridge;
        List<Waiter>? waiters = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            _initialCount = count;
            _currentCount = count;
            bridge = _waitHandle;
            if (count == 0)
            {
                waiters = TakeWaitersUnderLock();
            }
        }

        if (bridge is not null)
        {
            ControlledWaitHandle.UpdateBridgeSignal(bridge, count == 0);
        }

        CompleteWaiters(waiters, exception: null);
    }

    /// <summary>Waits until the countdown reaches zero.</summary>
    public void Wait() => WaitCore(Timeout.Infinite, returnsBoolean: false, cancellationToken: CancellationToken.None);

    /// <summary>Waits until the countdown reaches zero or cancellation is requested.</summary>
    public void Wait(CancellationToken cancellationToken) => WaitCore(Timeout.Infinite, returnsBoolean: false, cancellationToken: cancellationToken);

    /// <summary>Waits until the countdown reaches zero or the virtual deadline elapses.</summary>
    public bool Wait(int millisecondsTimeout) => WaitCore(millisecondsTimeout, returnsBoolean: true, cancellationToken: CancellationToken.None);

    /// <summary>Waits until the countdown reaches zero, cancellation, or the virtual deadline elapses.</summary>
    public bool Wait(int millisecondsTimeout, CancellationToken cancellationToken) =>
        WaitCore(millisecondsTimeout, returnsBoolean: true, cancellationToken: cancellationToken);

    /// <summary>Waits until the countdown reaches zero or the virtual deadline elapses.</summary>
    public bool Wait(TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        return WaitCore(ToMilliseconds(timeout), returnsBoolean: true, cancellationToken: CancellationToken.None);
    }

    /// <summary>Waits until the countdown reaches zero, cancellation, or the virtual deadline elapses.</summary>
    public bool Wait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        return WaitCore(ToMilliseconds(timeout), returnsBoolean: true, cancellationToken: cancellationToken);
    }

    /// <summary>Disposes this countdown event and faults blocked waiters.</summary>
    public void Dispose()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".Dispose");
        List<Waiter> waiters;
        WaitHandle? bridge;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            waiters = TakeWaitersUnderLock();
            bridge = _waitHandle;
        }

        CompleteWaiters(waiters, new ObjectDisposedException(nameof(ControlledCountdownEvent)));
        ControlledWaitHandle.DisposeBridge(bridge);
    }

    private bool AddCountCore(int signalCount, bool throwIfSet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(signalCount, 1);

        WaitHandle? bridge = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_currentCount == 0)
            {
                if (throwIfSet)
                {
                    throw new InvalidOperationException("The countdown event is already set.");
                }

                return false;
            }

            if (signalCount > int.MaxValue - _currentCount)
            {
                throw new InvalidOperationException("The resulting count exceeds Int32.MaxValue.");
            }

            _currentCount += signalCount;
            bridge = _waitHandle;
        }

        if (bridge is not null)
        {
            ControlledWaitHandle.UpdateBridgeSignal(bridge, signaled: false);
        }

        return true;
    }

    private bool WaitCore(int millisecondsTimeout, bool returnsBoolean, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(WaitApi);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(millisecondsTimeout);

        Waiter? waiter = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_currentCount == 0)
            {
                return true;
            }

            if (millisecondsTimeout == 0)
            {
                return false;
            }

            waiter = new Waiter();
            _waiters.Add(waiter);
            if (millisecondsTimeout != Timeout.Infinite)
            {
                waiter.Deadline = ControlledTaskRuntime.RegisterTimeout(
                    TimeSpan.FromMilliseconds(millisecondsTimeout),
                    () => TimeoutWaiter(waiter),
                    WaitApi);
            }
        }

        AttachCancellation(waiter!, cancellationToken);
        ControlledTaskRuntime.DrainUntil(() => waiter!.Completion.Task.IsCompleted, WaitApi);
        try
        {
            return waiter!.Completion.Task.GetAwaiter().GetResult();
        }
        catch (TimeoutException) when (returnsBoolean)
        {
            return false;
        }
    }

    private void AttachCancellation(Waiter waiter, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                var (countdown, pendingWaiter, token) = ((ControlledCountdownEvent, Waiter, CancellationToken))state!;
                countdown.CancelWaiter(pendingWaiter, token);
            },
            (this, waiter, cancellationToken));

        bool disposeRegistration;
        lock (_gate)
        {
            disposeRegistration = !waiter.Pending;
            if (!disposeRegistration)
            {
                waiter.Registration = registration;
            }
        }

        if (disposeRegistration)
        {
            registration.Dispose();
        }
    }

    private void TimeoutWaiter(Waiter waiter) => CompleteFailedWaiter(waiter, null, timedOut: true);

    private void CancelWaiter(Waiter waiter, CancellationToken cancellationToken) =>
        CompleteFailedWaiter(waiter, cancellationToken, timedOut: false);

    private void CompleteFailedWaiter(Waiter waiter, CancellationToken? cancellationToken, bool timedOut)
    {
        CancellationTokenRegistration registration;
        ISimulationTimer? deadline;
        lock (_gate)
        {
            if (!waiter.Pending || !_waiters.Remove(waiter))
            {
                return;
            }

            waiter.Pending = false;
            registration = waiter.Registration;
            waiter.Registration = default;
            deadline = waiter.Deadline;
            waiter.Deadline = null;
        }

        if (!timedOut)
        {
            deadline?.Cancel();
        }

        registration.Dispose();
        if (timedOut)
        {
            waiter.Completion.TrySetResult(false);
        }
        else
        {
            waiter.Completion.TrySetCanceled(cancellationToken!.Value);
        }
    }

    private List<Waiter> TakeWaitersUnderLock()
    {
        List<Waiter> result = [.. _waiters];
        _waiters.Clear();
        foreach (Waiter waiter in result)
        {
            waiter.Pending = false;
        }

        return result;
    }

    private static void CompleteWaiters(IEnumerable<Waiter>? waiters, Exception? exception)
    {
        if (waiters is null)
        {
            return;
        }

        foreach (Waiter waiter in waiters)
        {
            waiter.Deadline?.Cancel();
            waiter.Deadline = null;
            waiter.Registration.Dispose();
            waiter.Registration = default;
            if (exception is null)
            {
                waiter.Completion.TrySetResult(true);
            }
            else
            {
                waiter.Completion.TrySetException(exception);
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, typeof(ControlledCountdownEvent));

    private static void ValidateCount(int count, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, parameterName);
    }

    private static void ValidateTimeout(int millisecondsTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, Timeout.Infinite);
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        long milliseconds = (long)timeout.TotalMilliseconds;
        if (milliseconds < Timeout.Infinite || milliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return (int)milliseconds;
    }
}
