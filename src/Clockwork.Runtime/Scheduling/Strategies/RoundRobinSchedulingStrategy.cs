namespace Clockwork.Runtime.Scheduling.Strategies;

/// <summary>
/// The default policy, identical to the Phase 3A scheduler: run the runnable operation whose id is
/// the smallest strictly greater than the last-selected id, wrapping to the smallest runnable id when
/// none is greater. This rotates fairly through concurrently-runnable operations without starving any
/// of them, and is fully deterministic given the runnable set and last-selected id, so it needs no
/// recording to reproduce.
/// </summary>
public sealed class RoundRobinSchedulingStrategy : IControlledSchedulingStrategy
{
    /// <inheritdoc/>
    public string Name => "round-robin";

    /// <inheritdoc/>
    public bool RecordsNondeterministicChoices => false;

    /// <inheritdoc/>
    public ControlledOperation ChooseNext(ControlledSchedulingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ControlledOperation? firstAfterLast = null;
        foreach (var operation in context.Runnable)
        {
            if (operation.Id > context.LastSelected)
            {
                firstAfterLast = operation;
                break;
            }
        }

        // Runnable is ascending by id and non-empty, so index 0 is the wrap target.
        return firstAfterLast ?? context.Runnable[0];
    }
}
