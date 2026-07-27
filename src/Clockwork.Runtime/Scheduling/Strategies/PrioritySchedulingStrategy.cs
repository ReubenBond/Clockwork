namespace Clockwork.Runtime.Scheduling.Strategies;

/// <summary>
/// Runs the runnable operation with the highest <see cref="ControlledOperation.Priority"/>, breaking
/// ties with round-robin so equal-priority operations still rotate fairly and none starves within its
/// priority band. Priority is a crisp caller-supplied integer, so this policy is fully deterministic
/// and needs no recording. It deliberately does not model BCL thread-priority semantics; it is a
/// determinism tool for steering which ready operation runs first.
/// </summary>
public sealed class PrioritySchedulingStrategy : IControlledSchedulingStrategy
{
    private static readonly RoundRobinSchedulingStrategy TieBreak = new();

    /// <inheritdoc/>
    public string Name => "priority";

    /// <inheritdoc/>
    public bool RecordsNondeterministicChoices => false;

    /// <inheritdoc/>
    public ControlledOperation ChooseNext(ControlledSchedulingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var highest = int.MinValue;
        foreach (var operation in context.Runnable)
        {
            if (operation.Priority > highest)
            {
                highest = operation.Priority;
            }
        }

        // Restrict to the highest-priority band (preserving ascending-id order), then apply
        // round-robin within it so the choice is fair and stable.
        var topBand = new List<ControlledOperation>(context.Runnable.Count);
        foreach (var operation in context.Runnable)
        {
            if (operation.Priority == highest)
            {
                topBand.Add(operation);
            }
        }

        return TieBreak.ChooseNext(new ControlledSchedulingContext(topBand, context.LastSelected));
    }
}
