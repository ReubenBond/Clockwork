namespace Clockwork.Runtime;

/// <summary>
/// <para>
/// Marker type documenting this project's role. As of Phase 2 of the deterministic
/// instrumentation roadmap, <c>Clockwork.Runtime</c> hosts deterministic instrumentation
/// <em>runtime plumbing</em>: the ambient <see cref="Execution.SimulationExecutionContext"/>,
/// secure simulation activation (<see cref="Execution.SimulationActivationToken"/>), the
/// per-domain seed/decision service (<see cref="Random.SimulationSeedAuthority"/>), the typed
/// deterministic decision-log/replay contracts (see the <c>Clockwork.Runtime.Decisions</c>
/// namespace), and the API policy classification model (see the <c>Clockwork.Runtime.Policy</c>
/// namespace).
/// </para>
/// <para>
/// This project deliberately has no dependencies, so the deterministic simulation kernel
/// (currently in <c>src/Clockwork/Clockwork.csproj</c>, packaged as Clockwork.Simulation) can depend on it
/// without any circularity - and so that <c>Clockwork.Instrumentation</c> and
/// <c>Clockwork.Testing</c> can depend on it without depending on each other. The kernel itself
/// (clock, task queue, scheduler, network, node
/// lifecycle) has not migrated here yet - that remains a later phase. See
/// docs/compatibility.md for the overall roadmap and exactly what Phase 2 does and does not
/// implement (in particular: no Cecil/IL rewriting, and no controlled-operation scheduler yet).
/// </para>
/// </summary>
public static class RuntimeBoundary
{
}
