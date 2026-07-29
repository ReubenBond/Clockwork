namespace Clockwork.Runtime.Tasks;

/// <summary>
/// Shared lifecycle for a waiter owned by a simulated synchronization primitive.
/// The owning primitive serializes queue mutation; this type owns completion and
/// detaches cancellation and deadline registrations when the waiter is claimed.
/// </summary>
internal class SimulationWaiter
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private CancellationTokenRegistration _cancellationRegistration;
    private ISimulationTimer? _deadline;
    private bool _pending = true;

    /// <summary>Gets the task completed by the first terminal waiter outcome.</summary>
    public Task<bool> Task => _completion.Task;

    /// <summary>Gets whether no terminal outcome has claimed this waiter.</summary>
    public bool IsPending => _pending;

    /// <summary>Attaches a deterministic deadline while the owning primitive is serialized.</summary>
    public static void AttachDeadline<TWaiter>(
        TWaiter waiter,
        TimeSpan timeout,
        string api,
        Action<TWaiter> onElapsed)
        where TWaiter : SimulationWaiter
    {
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(onElapsed);
        waiter._deadline = SimulationTaskRuntime.RegisterTimeout(
            timeout,
            () => onElapsed(waiter),
            api);
    }

    /// <summary>
    /// Attaches cancellation without losing cancellation which races registration.
    /// Registration disposal always occurs outside <paramref name="gate"/>.
    /// </summary>
    public static void AttachCancellation<TWaiter>(
        object gate,
        TWaiter waiter,
        Action<TWaiter, CancellationToken> onCancellation,
        CancellationToken cancellationToken)
        where TWaiter : SimulationWaiter
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(onCancellation);
        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        var callbackState = new CancellationCallbackState<TWaiter>(
            waiter,
            onCancellation,
            cancellationToken);
        CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                var callback = (CancellationCallbackState<TWaiter>)state!;
                callback.OnCancellation(callback.Waiter, callback.CancellationToken);
            },
            callbackState);

        bool disposeRegistration;
        lock (gate)
        {
            disposeRegistration = !waiter._pending;
            if (!disposeRegistration)
            {
                waiter._cancellationRegistration = registration;
            }
        }

        if (disposeRegistration)
        {
            registration.Dispose();
        }
    }

    /// <summary>Claims and removes one pending waiter while its owning queue is serialized.</summary>
    public static bool TryTake<TWaiter>(
        List<TWaiter> waiters,
        TWaiter waiter,
        out Claim claim,
        bool cancelDeadline = true)
        where TWaiter : SimulationWaiter
    {
        ArgumentNullException.ThrowIfNull(waiters);
        ArgumentNullException.ThrowIfNull(waiter);
        if (!waiter._pending || !waiters.Remove(waiter))
        {
            claim = default;
            return false;
        }

        claim = waiter.ClaimCore(cancelDeadline);
        return true;
    }

    /// <summary>Claims and removes every pending waiter while its owning queue is serialized.</summary>
    public static List<Claim> TakeAll<TWaiter>(List<TWaiter> waiters)
        where TWaiter : SimulationWaiter
    {
        ArgumentNullException.ThrowIfNull(waiters);
        var claims = new List<Claim>(waiters.Count);
        foreach (TWaiter waiter in waiters)
        {
            if (waiter._pending)
            {
                claims.Add(waiter.ClaimCore(cancelDeadline: true));
            }
        }

        waiters.Clear();
        return claims;
    }

    /// <summary>
    /// Claims a waiter whose owner needs to remove it from more than one queue.
    /// Must be called while all owning queues are serialized.
    /// </summary>
    public bool TryClaim(out Claim claim, bool cancelDeadline = true)
    {
        if (!_pending)
        {
            claim = default;
            return false;
        }

        claim = ClaimCore(cancelDeadline);
        return true;
    }

    private Claim ClaimCore(bool cancelDeadline)
    {
        _pending = false;
        CancellationTokenRegistration registration = _cancellationRegistration;
        _cancellationRegistration = default;
        ISimulationTimer? deadline = _deadline;
        _deadline = null;
        return new Claim(this, registration, cancelDeadline ? deadline : null);
    }

    private sealed class CancellationCallbackState<TWaiter>(
        TWaiter waiter,
        Action<TWaiter, CancellationToken> onCancellation,
        CancellationToken cancellationToken)
        where TWaiter : SimulationWaiter
    {
        public TWaiter Waiter { get; } = waiter;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public Action<TWaiter, CancellationToken> OnCancellation { get; } = onCancellation;
    }

    /// <summary>
    /// Resources detached by the first terminal outcome. Complete outside the owning
    /// primitive's lock so registration disposal and continuations cannot re-enter it.
    /// </summary>
    internal readonly struct Claim(
        SimulationWaiter waiter,
        CancellationTokenRegistration cancellationRegistration,
        ISimulationTimer? deadline)
    {
        private readonly SimulationWaiter _waiter = waiter;
        private readonly CancellationTokenRegistration _cancellationRegistration = cancellationRegistration;
        private readonly ISimulationTimer? _deadline = deadline;

        public Task<bool> Task => _waiter.Task;

        public void Complete(bool result)
        {
            Cleanup();
            _waiter._completion.TrySetResult(result);
        }

        public void Cancel(CancellationToken cancellationToken)
        {
            Cleanup();
            _waiter._completion.TrySetCanceled(cancellationToken);
        }

        public void Fault(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Cleanup();
            _waiter._completion.TrySetException(exception);
        }

        private void Cleanup()
        {
            _deadline?.Cancel();
            _cancellationRegistration.Dispose();
        }
    }
}
