namespace Clockwork.Runtime.Scheduling.Strategies;

/// <summary>
/// The legacy/compatibility policy: always run the runnable operation with the smallest id. Because
/// operations are assigned ids in registration order and a compat-bridge operation typically runs to
/// completion before the next is admitted, this reproduces the simple "first registered, first run"
/// ordering that the legacy task-queue bridge relied on. It is fully deterministic and needs no
/// recording.
/// </summary>
public sealed class FifoSchedulingStrategy : IControlledSchedulingStrategy
{
    /// <inheritdoc/>
    public string Name => "fifo";

    /// <inheritdoc/>
    public bool RecordsNondeterministicChoices => false;

    /// <inheritdoc/>
    public ControlledOperation ChooseNext(ControlledSchedulingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Runnable is ascending by id, so the first element is the smallest id.
        return context.Runnable[0];
    }
}
