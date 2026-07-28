namespace Clockwork.Runtime.Policy;

/// <summary>
/// The result of resolving a <see cref="SimulationApiKey"/> (or an assembly name) against a
/// <see cref="SimulationApiPolicyRegistry"/>: the applicable policy, plus a human-readable
/// explanation of why - which precedence tier (per-API override, per-assembly override, or
/// registry default) produced it, and any caller-supplied reason for that override.
/// </summary>
/// <param name="Policy">The resolved policy.</param>
/// <param name="Reason">A human-readable explanation of why this policy applies.</param>
public readonly record struct SimulationApiPolicyDecision(SimulationApiPolicy Policy, string Reason)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Policy} ({Reason})";
}
