using System.Threading.Tasks;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Shims.System.Threading;

/// <summary>A fully controlled replacement for <see cref="global::System.Threading.Barrier"/>.</summary>
public sealed class ControlledBarrier : IDisposable
{
    private const int MaximumParticipants = 0x7fff;
    private const string TypeName = "System.Threading.Barrier";
    private const string SignalAndWaitApi = TypeName + ".SignalAndWait";

    private sealed class Waiter
    {
        public TaskCompletionSource<bool> Completion { get; } = new();

        public long StrandId { get; init; }

        public long Phase { get; init; }

        public CancellationTokenRegistration Registration;

        public ISimulationTimer? Deadline;

        public bool Pending { get; set; } = true;
    }

    private readonly object _gate = new();
    private readonly Action<ControlledBarrier>? _postPhaseAction;
    private readonly List<Waiter> _waiters = [];
    private readonly HashSet<long> _arrivedStrands = [];
    private bool _disposed;
    private bool _executingPostPhaseAction;
    private int _participantCount;
    private int _participantsRemaining;
    private long _currentPhaseNumber;

    /// <summary>Initializes a controlled barrier.</summary>
    public ControlledBarrier(int participantCount)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + "..ctor");
        ValidateInitialParticipantCount(participantCount);
        _participantCount = participantCount;
        _participantsRemaining = participantCount;
    }

    /// <summary>Initializes a controlled barrier with a post-phase callback.</summary>
    public ControlledBarrier(int participantCount, Action<ControlledBarrier>? postPhaseAction)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + "..ctor");
        ValidateInitialParticipantCount(participantCount);
        _participantCount = participantCount;
        _participantsRemaining = participantCount;
        _postPhaseAction = postPhaseAction;
    }

    /// <summary>Gets the completed phase count.</summary>
    public long CurrentPhaseNumber
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_CurrentPhaseNumber");
            lock (_gate)
            {
                return _currentPhaseNumber;
            }
        }
    }

    /// <summary>Gets the registered participant count.</summary>
    public int ParticipantCount
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_ParticipantCount");
            lock (_gate)
            {
                return _participantCount;
            }
        }
    }

    /// <summary>Gets the participants still required to finish the current phase.</summary>
    public int ParticipantsRemaining
    {
        get
        {
            SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".get_ParticipantsRemaining");
            lock (_gate)
            {
                return _participantsRemaining;
            }
        }
    }

    /// <summary>Adds one participant and returns the phase in which it joined.</summary>
    public long AddParticipant() => AddParticipantsCore(1, TypeName + ".AddParticipant");

    /// <summary>Adds participants and returns the phase in which they joined.</summary>
    public long AddParticipants(int participantCount) => AddParticipantsCore(participantCount, TypeName + ".AddParticipants");

    /// <summary>Removes one participant.</summary>
    public void RemoveParticipant() => RemoveParticipantsCore(1, TypeName + ".RemoveParticipant");

    /// <summary>Removes participants.</summary>
    public void RemoveParticipants(int participantCount) => RemoveParticipantsCore(participantCount, TypeName + ".RemoveParticipants");

    /// <summary>Signals arrival and waits for the phase to complete.</summary>
    public void SignalAndWait() => SignalAndWaitCore(Timeout.Infinite, returnsBoolean: false, cancellationToken: CancellationToken.None);

    /// <summary>Signals arrival and waits for the phase to complete or cancellation.</summary>
    public void SignalAndWait(CancellationToken cancellationToken) =>
        SignalAndWaitCore(Timeout.Infinite, returnsBoolean: false, cancellationToken: cancellationToken);

    /// <summary>Signals arrival and waits for the phase to complete or the virtual deadline.</summary>
    public bool SignalAndWait(int millisecondsTimeout) =>
        SignalAndWaitCore(millisecondsTimeout, returnsBoolean: true, cancellationToken: CancellationToken.None);

    /// <summary>Signals arrival and waits for the phase to complete, cancellation, or the virtual deadline.</summary>
    public bool SignalAndWait(int millisecondsTimeout, CancellationToken cancellationToken) =>
        SignalAndWaitCore(millisecondsTimeout, returnsBoolean: true, cancellationToken: cancellationToken);

    /// <summary>Signals arrival and waits for the phase to complete or the virtual deadline.</summary>
    public bool SignalAndWait(TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        return SignalAndWaitCore(ToMilliseconds(timeout), returnsBoolean: true, cancellationToken: CancellationToken.None);
    }

    /// <summary>Signals arrival and waits for the phase to complete, cancellation, or the virtual deadline.</summary>
    public bool SignalAndWait(TimeSpan timeout, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        return SignalAndWaitCore(ToMilliseconds(timeout), returnsBoolean: true, cancellationToken: cancellationToken);
    }

    /// <summary>Disposes this barrier and faults any phase waiters.</summary>
    public void Dispose()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(TypeName + ".Dispose");
        List<Waiter> waiters;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ThrowIfInPostPhaseAction();
            _disposed = true;
            waiters = TakeWaitersUnderLock();
            _arrivedStrands.Clear();
        }

        CompleteWaiters(waiters, new ObjectDisposedException(nameof(ControlledBarrier)));
    }

    private long AddParticipantsCore(int participantCount, string api)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(api);
        ValidateAddedOrRemovedParticipantCount(participantCount);
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfInPostPhaseAction();
            if (_participantCount > MaximumParticipants - participantCount)
            {
                if (api.EndsWith(".AddParticipant", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"The total participant count cannot exceed {MaximumParticipants}.");
                }

                throw new ArgumentOutOfRangeException(
                    nameof(participantCount),
                    participantCount,
                    $"The total participant count cannot exceed {MaximumParticipants}.");
            }

            _participantCount += participantCount;
            _participantsRemaining += participantCount;
            return _currentPhaseNumber;
        }
    }

    private void RemoveParticipantsCore(int participantCount, string api)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(api);
        ValidateAddedOrRemovedParticipantCount(participantCount);
        List<Waiter>? completed = null;
        Exception? postPhaseException = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfInPostPhaseAction();
            if (participantCount > _participantCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(participantCount),
                    participantCount,
                    "The participant count to remove exceeds the number of registered participants.");
            }

            if (participantCount > _participantsRemaining)
            {
                throw new InvalidOperationException("Participants which have already arrived cannot be removed from the current phase.");
            }

            _participantCount -= participantCount;
            _participantsRemaining -= participantCount;
            if (_participantCount > 0 && _participantsRemaining == 0)
            {
                completed = [.. _waiters];
            }
        }

        if (completed is not null)
        {
            postPhaseException = FinishPhase();
            CompleteWaiters(completed, postPhaseException);
            if (postPhaseException is not null)
            {
                throw postPhaseException;
            }
        }
    }

    private bool SignalAndWaitCore(int millisecondsTimeout, bool returnsBoolean, CancellationToken cancellationToken)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SignalAndWaitApi);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTimeout(millisecondsTimeout);

        Waiter? waiter = null;
        List<Waiter>? completed = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfInPostPhaseAction();
            long strandId = SimulationSynchronizationFlow.CurrentId;
            if (!_arrivedStrands.Add(strandId))
            {
                throw new InvalidOperationException("The current strand has already arrived at this barrier phase.");
            }

            if (_participantCount == 0)
            {
                _arrivedStrands.Remove(strandId);
                throw new InvalidOperationException("The barrier has no registered participants.");
            }

            _participantsRemaining--;
            if (_participantsRemaining == 0)
            {
                completed = [.. _waiters];
            }
            else if (millisecondsTimeout == 0)
            {
                _participantsRemaining++;
                _arrivedStrands.Remove(strandId);
                return false;
            }
            else
            {
                waiter = new Waiter { StrandId = strandId, Phase = _currentPhaseNumber };
                _waiters.Add(waiter);
                if (millisecondsTimeout != Timeout.Infinite)
                {
                    waiter.Deadline = SimulationTaskRuntime.RegisterTimeout(
                        TimeSpan.FromMilliseconds(millisecondsTimeout),
                        () => TimeoutWaiter(waiter),
                        SignalAndWaitApi);
                }
            }
        }

        if (completed is not null)
        {
            Exception? exception = FinishPhase();
            CompleteWaiters(completed, exception);
            if (exception is not null)
            {
                throw exception;
            }

            return true;
        }

        AttachCancellation(waiter!, cancellationToken);
        SimulationTaskRuntime.DrainUntil(() => waiter!.Completion.Task.IsCompleted, SignalAndWaitApi);
        try
        {
            return waiter!.Completion.Task.GetAwaiter().GetResult();
        }
        catch (TimeoutException) when (returnsBoolean)
        {
            return false;
        }
    }

    private Exception? FinishPhase()
    {
        Exception? exception = null;
        try
        {
            lock (_gate)
            {
                _executingPostPhaseAction = true;
            }

            _postPhaseAction?.Invoke(this);
        }
        catch (Exception caught)
        {
            exception = new BarrierPostPhaseException(caught);
        }
        finally
        {
            lock (_gate)
            {
                _executingPostPhaseAction = false;
                _currentPhaseNumber++;
                _participantsRemaining = _participantCount;
                _arrivedStrands.Clear();
                foreach (Waiter waiter in _waiters)
                {
                    waiter.Pending = false;
                }

                _waiters.Clear();
            }
        }

        return exception;
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
                var (barrier, pendingWaiter, token) = ((ControlledBarrier, Waiter, CancellationToken))state!;
                barrier.CancelWaiter(pendingWaiter, token);
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
            _participantsRemaining++;
            _arrivedStrands.Remove(waiter.StrandId);
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

    private static void CompleteWaiters(IEnumerable<Waiter> waiters, Exception? exception)
    {
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, typeof(ControlledBarrier));

    private void ThrowIfInPostPhaseAction()
    {
        if (_executingPostPhaseAction)
        {
            throw new InvalidOperationException("Barrier operations are not permitted from a post-phase action.");
        }
    }

    private static void ValidateInitialParticipantCount(int participantCount)
    {
        if (participantCount < 0 || participantCount > MaximumParticipants)
        {
            throw new ArgumentOutOfRangeException(
                nameof(participantCount),
                participantCount,
                $"The participant count must be between 1 and {MaximumParticipants}.");
        }
    }

    private static void ValidateAddedOrRemovedParticipantCount(int participantCount)
    {
        if (participantCount < 1 || participantCount > MaximumParticipants)
        {
            throw new ArgumentOutOfRangeException(
                nameof(participantCount),
                participantCount,
                $"The participant count must be between 1 and {MaximumParticipants}.");
        }
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
