using System.Globalization;

namespace Clockwork.Runtime.Scheduling;

/// <summary>
/// Base class for exceptions that report misuse of the controlled-operation kernel (for example an
/// illegal state transition). These are genuine programming errors surfaced to the caller; they are
/// distinct from the internal scheduler-control unwinding signal used during teardown, which is not
/// a public type and must never be caught by user code.
/// </summary>
public class ControlledOperationException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="ControlledOperationException"/> class.</summary>
    public ControlledOperationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ControlledOperationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ControlledOperationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ControlledOperationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ControlledOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the scheduler is asked (directly or via a mis-sequenced operation callback) to move a
/// <see cref="ControlledOperation"/> through a transition that is not in the fixed legality table
/// (see <see cref="ControlledOperationState"/>). This always indicates a kernel bug or a misuse of
/// the pause/resume primitives; it is deliberately loud (rather than a silent no-op) so such bugs
/// surface immediately with the operation identity and the exact rejected edge.
/// </summary>
public sealed class InvalidControlledOperationTransitionException : ControlledOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidControlledOperationTransitionException"/>
    /// class describing a rejected transition.
    /// </summary>
    /// <param name="operationId">The operation whose transition was rejected.</param>
    /// <param name="from">The state the operation was in.</param>
    /// <param name="to">The state that was illegally requested.</param>
    public InvalidControlledOperationTransitionException(ControlledOperationId operationId, ControlledOperationState from, ControlledOperationState to)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"Illegal controlled-operation transition for {operationId}: {from} -> {to}. This transition is not permitted by the operation state machine."))
    {
        OperationId = operationId;
        From = from;
        To = to;
    }

    /// <summary>Gets the operation whose transition was rejected.</summary>
    public ControlledOperationId OperationId { get; }

    /// <summary>Gets the state the operation was in.</summary>
    public ControlledOperationState From { get; }

    /// <summary>Gets the state that was illegally requested.</summary>
    public ControlledOperationState To { get; }
}

/// <summary>
/// <para>
/// The internal control-flow signal thrown inside a paused/parked operation's physical thread when
/// the scheduler is tearing it down (see <see cref="ControlledOperationScheduler.Dispose"/> and
/// cancellation of a not-yet-completed operation). It exists purely to unwind the operation body's
/// stack so the physical thread can exit without being stranded and without resorting to an unsafe
/// <c>Thread.Abort</c>.
/// </para>
/// <para>
/// This type is deliberately <see langword="internal"/>: SUT/operation code cannot reference it by
/// name, and it is not a subclass of any exception type user code would deliberately filter for. If
/// operation code nonetheless swallows it via a broad <c>catch</c>, the kernel still cannot be
/// stranded - once teardown has been requested every subsequent attempt to park re-throws it, and
/// the scheduler joins the physical thread with a bounded timeout - but well-behaved code must let
/// it propagate.
/// </para>
/// </summary>
internal sealed class ControlledOperationAbortSignal : Exception
{
    public ControlledOperationAbortSignal(ControlledOperationId operationId)
        : base(string.Create(CultureInfo.InvariantCulture, $"Controlled operation {operationId} is being torn down."))
    {
        OperationId = operationId;
    }

    public ControlledOperationId OperationId { get; }
}
