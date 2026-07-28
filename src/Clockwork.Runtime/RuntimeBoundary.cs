namespace Clockwork.Runtime;

/// <summary>
/// <para>
/// Marker type documenting this project's role. <c>Clockwork.Runtime</c> hosts deterministic
/// instrumentation runtime services: the ambient <see cref="Execution.SimulationExecutionContext"/>,
/// secure simulation activation (<see cref="Execution.SimulationActivationToken"/>), the
/// per-domain seed/decision service (<see cref="Random.SimulationSeedAuthority"/>), the typed
/// deterministic decision-log/replay contracts (see the <c>Clockwork.Runtime.Decisions</c>
/// namespace), and the API policy classification model (see the <c>Clockwork.Runtime.Policy</c>
/// namespace).
/// </para>
/// <para>
/// This project deliberately has no dependencies, so the deterministic simulation kernel
/// (in <c>src/Clockwork/Clockwork.csproj</c>, packaged as Clockwork.Simulation) can depend on it
/// without any circularity - and so that <c>Clockwork.Instrumentation</c> and
/// <c>Clockwork.Testing</c> can depend on it without depending on each other. The kernel itself
/// (clock, task queue, scheduler, network, node
/// lifecycle) remains in the simulation package. See docs/compatibility.md for the supported
/// instrumentation and runtime boundaries.
/// </para>
/// </summary>
public static class RuntimeBoundary
{
}
