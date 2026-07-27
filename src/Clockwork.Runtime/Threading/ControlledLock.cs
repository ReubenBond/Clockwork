using System.Threading;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// The controlled stand-in for <see cref="System.Threading.Lock"/> (the .NET 9+ dedicated lock type).
/// The rewriter substitutes the type at every use site - <c>new Lock()</c> becomes
/// <c>new ControlledLock()</c>, and the C# <c>lock (lockObj)</c> statement (which the compiler lowers to
/// <c>Lock.Scope scope = lockObj.EnterScope(); try { ... } finally { scope.Dispose(); }</c>) is
/// redirected to <see cref="EnterScope"/> and <see cref="Scope.Dispose"/> on this type. The public
/// surface therefore mirrors <see cref="System.Threading.Lock"/> exactly, including the nested
/// <see cref="Scope"/> ref struct.
/// </para>
/// <para>
/// Inside a simulation the lock is modelled on the controlled monitor kernel
/// (<see cref="ControlledMonitor"/>) against a private key object, so acquisition, mutual exclusion,
/// reentrancy and deadlock detection all behave exactly as a controlled <c>Monitor</c>. Outside a
/// simulation every operation delegates to a real wrapped <see cref="System.Threading.Lock"/>, so
/// production behaviour (including the real type's thread-affinity and reentrancy) is preserved.
/// </para>
/// <para>
/// <see cref="System.Threading.Lock"/> exposes no OS-only members, so the whole surface is controlled;
/// there is nothing to reject.
/// </para>
/// </summary>
public sealed class ControlledLock
{
    // Delegated to outside a simulation so production keeps the real dedicated-lock semantics.
    private readonly Lock _real = new();

    // The controlled-monitor key used inside a simulation. A dedicated private object keeps the lock's
    // identity distinct from the ControlledLock instance itself.
    private readonly object _key = new();

    /// <summary>Initializes a new controlled lock, mirroring <c>new System.Threading.Lock()</c>.</summary>
    public ControlledLock()
    {
    }

    /// <summary>Gets a value indicating whether the current strand holds the lock.</summary>
    public bool IsHeldByCurrentThread =>
        ControlledTaskRuntime.IsSimulationActive ? ControlledMonitor.IsEntered(_key) : _real.IsHeldByCurrentThread;

    /// <summary>Controlled <see cref="System.Threading.Lock.Enter()"/>.</summary>
    public void Enter()
    {
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            _real.Enter();
            return;
        }

        ControlledMonitor.Enter(_key);
    }

    /// <summary>Controlled <see cref="System.Threading.Lock.Exit()"/>.</summary>
    public void Exit()
    {
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            _real.Exit();
            return;
        }

        ControlledMonitor.Exit(_key);
    }

    /// <summary>
    /// Controlled <see cref="System.Threading.Lock.EnterScope()"/>: acquires the lock and returns a
    /// <see cref="Scope"/> whose disposal releases it (the target of the C# <c>lock</c> lowering).
    /// </summary>
    /// <returns>A scope that releases the lock when disposed.</returns>
    public Scope EnterScope()
    {
        Enter();
        return new Scope(this);
    }

    /// <summary>Controlled <see cref="System.Threading.Lock.TryEnter()"/>: a non-blocking acquire attempt.</summary>
    /// <returns><see langword="true"/> if the lock was acquired.</returns>
    public bool TryEnter()
    {
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return _real.TryEnter();
        }

        return ControlledMonitor.TryEnter(_key);
    }

    /// <summary>Controlled <see cref="System.Threading.Lock.TryEnter(int)"/>.</summary>
    /// <param name="millisecondsTimeout">Zero for a non-blocking try; -1 or a finite positive value to block. Finite positive timeouts are modelled as infinite inside a simulation (virtual-time timeouts are Phase 8).</param>
    /// <returns><see langword="true"/> if the lock was acquired.</returns>
    public bool TryEnter(int millisecondsTimeout)
    {
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return _real.TryEnter(millisecondsTimeout);
        }

        return ControlledMonitor.TryEnter(_key, millisecondsTimeout);
    }

    /// <summary>Controlled <see cref="System.Threading.Lock.TryEnter(TimeSpan)"/>.</summary>
    /// <param name="timeout">The timeout, interpreted as for <see cref="TryEnter(int)"/>.</param>
    /// <returns><see langword="true"/> if the lock was acquired.</returns>
    public bool TryEnter(TimeSpan timeout)
    {
        if (!ControlledTaskRuntime.IsSimulationActive)
        {
            return _real.TryEnter(timeout);
        }

        return ControlledMonitor.TryEnter(_key, timeout);
    }

    /// <summary>
    /// The controlled analogue of <see cref="System.Threading.Lock.Scope"/>: a ref struct returned by
    /// <see cref="EnterScope"/> whose <see cref="Dispose"/> releases the lock exactly once, matching the
    /// C# <c>lock</c> statement's <c>try/finally</c> lowering.
    /// </summary>
    public ref struct Scope
    {
        private ControlledLock? _owner;

        internal Scope(ControlledLock owner)
        {
            _owner = owner;
        }

        /// <summary>Releases the lock acquired by <see cref="EnterScope"/>. Idempotent.</summary>
        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.Exit();
        }
    }
}
