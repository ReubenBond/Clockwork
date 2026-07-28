namespace Clockwork.Runtime.Policy;

/// <summary>
/// <para>
/// Classifies how a specific API (or an entire assembly's worth of APIs) is treated while a simulation
/// is active. Rewrite rules and manifests use this stable policy model instead of hard-coding behavior
/// at every call site.
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
