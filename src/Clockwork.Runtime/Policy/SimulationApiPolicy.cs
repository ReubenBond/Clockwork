namespace Clockwork.Runtime.Policy;

/// <summary>
/// <para>
/// Classifies how a specific API (or an entire assembly's worth of APIs) should be treated by a
/// future interception layer while a simulation is active. This is a policy <em>data model</em>
/// only in Phase 2 - nothing in this project intercepts calls yet (no Cecil rewriting, no IL
/// weaving, no runtime hooking). <see cref="SimulationApiPolicyRegistry"/> exists so that future
/// interception (Phase 3+) has a stable, testable place to ask "what should happen here?" without
/// hard-coding the answer at every call site.
/// </para>
/// </summary>
public enum SimulationApiPolicy
{
    /// <summary>
    /// The API is intercepted and routed through deterministic simulation machinery (e.g. a
    /// virtual clock, a deterministic random source, a simulated network). This is the strict
    /// default while a simulation is active for any API not explicitly classified otherwise.
    /// </summary>
    Controlled,

    /// <summary>
    /// The API is forbidden while a simulation is active - calling it should fail loudly (e.g. by
    /// throwing) rather than silently doing something non-deterministic. Use this for APIs that
    /// cannot be made deterministic and must never be reached from simulated code (e.g. real
    /// wall-clock/network/filesystem access with no simulated equivalent).
    /// </summary>
    Rejected,

    /// <summary>
    /// The API is allowed to execute exactly as it would outside a simulation, with no
    /// interception. This must always be an explicit, intentional classification - see
    /// <see cref="SimulationApiPolicyRegistry"/> for why it can never be a default.
    /// </summary>
    PassThrough,
}
