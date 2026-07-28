using System.ComponentModel;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Racing;

/// <summary>
/// Bridges controlled synchronization transitions into the race detector's happens-before and
/// lockset model.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RaceSynchronization
{
    /// <summary>Records acquisition of an exclusive controlled synchronization object.</summary>
    public static void Enter(object synchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (ControlledOperationScheduler.TryGetExecutingOperation(out ControlledOperationScheduler? scheduler, out _))
        {
            scheduler.EnterRaceSynchronization(synchronization);
        }
    }

    /// <summary>Records release of an exclusive controlled synchronization object.</summary>
    public static void Exit(object synchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (ControlledOperationScheduler.TryGetExecutingOperation(out ControlledOperationScheduler? scheduler, out _))
        {
            scheduler.ExitRaceSynchronization(synchronization);
        }
    }

    /// <summary>Publishes a release/signal happens-before edge without holding a lockset entry.</summary>
    public static void Signal(object synchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (ControlledOperationScheduler.TryGetExecutingOperation(out ControlledOperationScheduler? scheduler, out _))
        {
            scheduler.SignalRaceSynchronization(synchronization);
        }
    }

    /// <summary>Consumes an acquire/wait happens-before edge without holding a lockset entry.</summary>
    public static void Wait(object synchronization)
    {
        ArgumentNullException.ThrowIfNull(synchronization);
        if (ControlledOperationScheduler.TryGetExecutingOperation(out ControlledOperationScheduler? scheduler, out _))
        {
            scheduler.WaitRaceSynchronization(synchronization);
        }
    }
}
