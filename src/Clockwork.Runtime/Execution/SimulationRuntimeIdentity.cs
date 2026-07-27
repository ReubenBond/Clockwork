namespace Clockwork.Runtime.Execution;

/// <summary>
/// <para>
/// Identifies one active simulation runtime (a "host" such as a simulation cluster) for the
/// lifetime of the ambient <see cref="SimulationExecutionContext"/>. Two runtimes are the same
/// runtime if and only if their <see cref="Id"/>s are equal - <see cref="Seed"/> and
/// <see cref="Description"/> are diagnostic metadata only and are not used for identity.
/// </para>
/// <para>
/// Instances are created once per simulation host (see <see cref="SimulationRuntimeActivation"/>)
/// and then threaded through every ambient scope that host installs, so code running anywhere
/// inside that simulation can discover which simulation (and, transitively, which seed) it is
/// executing under.
/// </para>
/// </summary>
/// <param name="Id">A process-unique identifier for this runtime instance.</param>
/// <param name="Seed">The deterministic root seed the runtime was created with, for diagnostics.</param>
/// <param name="Description">An optional human-readable description, for diagnostics only.</param>
public sealed record SimulationRuntimeIdentity(Guid Id, int Seed, string? Description = null)
{
    /// <inheritdoc />
    public bool Equals(SimulationRuntimeIdentity? other) => other is not null && Id == other.Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
