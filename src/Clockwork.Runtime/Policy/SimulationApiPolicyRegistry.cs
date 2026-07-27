using System.Collections.Concurrent;

namespace Clockwork.Runtime.Policy;

/// <summary>
/// <para>
/// Resolves the <see cref="SimulationApiPolicy"/> that applies to a given API while a simulation
/// is active, with deterministic precedence: an explicit per-API override wins over an explicit
/// per-assembly override, which wins over the registry's default policy.
/// </para>
/// <para>
/// This registry enforces one safety rule at construction: the default policy can never be
/// <see cref="SimulationApiPolicy.PassThrough"/>. Pass-through is inherently "skip determinism for
/// this API" - allowing it to be the silent fallback for every unclassified API would defeat the
/// purpose of classification entirely. Pass-through is only ever available as an explicit,
/// intentional per-assembly or per-API override.
/// </para>
/// <para>
/// This is a policy data model only in Phase 2 - nothing here intercepts calls. It exists so a
/// future interception layer (Phase 3+) has one deterministic place to ask "what should happen for
/// this API?" instead of hard-coding per-call-site logic.
/// </para>
/// </summary>
public sealed class SimulationApiPolicyRegistry
{
    private const string DefaultReason = "registry default policy";
    private const string AssemblyOverrideReason = "explicit per-assembly override";
    private const string ApiOverrideReason = "explicit per-API override";

    private readonly ConcurrentDictionary<string, (SimulationApiPolicy Policy, string? Reason)> _assemblyOverrides = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SimulationApiKey, (SimulationApiPolicy Policy, string? Reason)> _apiOverrides = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationApiPolicyRegistry"/> class.
    /// </summary>
    /// <param name="defaultPolicy">
    /// The policy applied when no per-assembly or per-API override matches. Defaults to
    /// <see cref="SimulationApiPolicy.Controlled"/> (the strict simulation default).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="defaultPolicy"/> is <see cref="SimulationApiPolicy.PassThrough"/> -
    /// pass-through must always be an explicit, targeted override, never a blanket default.
    /// </exception>
    public SimulationApiPolicyRegistry(SimulationApiPolicy defaultPolicy = SimulationApiPolicy.Controlled)
    {
        if (defaultPolicy == SimulationApiPolicy.PassThrough)
        {
            throw new ArgumentException(
                "PassThrough cannot be used as a registry default policy - it must always be an explicit per-assembly or per-API override while a simulation is active.",
                nameof(defaultPolicy));
        }

        DefaultPolicy = defaultPolicy;
    }

    /// <summary>
    /// Gets the policy applied when no per-assembly or per-API override matches.
    /// </summary>
    public SimulationApiPolicy DefaultPolicy { get; }

    /// <summary>
    /// Sets (or replaces) an explicit policy override for every API belonging to the named
    /// assembly, unless a more specific per-API override also matches (see <see cref="Resolve"/>).
    /// </summary>
    /// <param name="assemblyName">The simple assembly name, e.g. <c>"System.Net.Http"</c>.</param>
    /// <param name="policy">The policy to apply.</param>
    /// <param name="reason">An optional human-readable reason, surfaced via <see cref="SimulationApiPolicyDecision.Reason"/>.</param>
    public void SetAssemblyPolicy(string assemblyName, SimulationApiPolicy policy, string? reason = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        _assemblyOverrides[assemblyName] = (policy, reason);
    }

    /// <summary>
    /// Sets (or replaces) an explicit policy override for exactly one API, taking precedence over
    /// any per-assembly override and the registry default.
    /// </summary>
    /// <param name="api">The API to classify.</param>
    /// <param name="policy">The policy to apply.</param>
    /// <param name="reason">An optional human-readable reason, surfaced via <see cref="SimulationApiPolicyDecision.Reason"/>.</param>
    public void SetApiPolicy(SimulationApiKey api, SimulationApiPolicy policy, string? reason = null)
    {
        _apiOverrides[api] = (policy, reason);
    }

    /// <summary>
    /// Removes a previously set per-assembly override, reverting that assembly's APIs to the
    /// registry default (unless individual per-API overrides still apply).
    /// </summary>
    /// <param name="assemblyName">The simple assembly name to clear.</param>
    /// <returns><see langword="true"/> if an override was removed.</returns>
    public bool ClearAssemblyPolicy(string assemblyName) => _assemblyOverrides.TryRemove(assemblyName, out _);

    /// <summary>
    /// Removes a previously set per-API override, reverting that API to any matching per-assembly
    /// override or the registry default.
    /// </summary>
    /// <param name="api">The API to clear.</param>
    /// <returns><see langword="true"/> if an override was removed.</returns>
    public bool ClearApiPolicy(SimulationApiKey api) => _apiOverrides.TryRemove(api, out _);

    /// <summary>
    /// Resolves the policy that applies to <paramref name="api"/>, applying precedence:
    /// per-API override, then per-assembly override, then the registry default.
    /// </summary>
    /// <param name="api">The API to resolve a policy for.</param>
    /// <returns>The resolved policy and a diagnostic reason explaining which tier produced it.</returns>
    public SimulationApiPolicyDecision Resolve(SimulationApiKey api)
    {
        if (_apiOverrides.TryGetValue(api, out var apiOverride))
        {
            return new SimulationApiPolicyDecision(apiOverride.Policy, apiOverride.Reason ?? ApiOverrideReason);
        }

        if (_assemblyOverrides.TryGetValue(api.AssemblyName, out var assemblyOverride))
        {
            return new SimulationApiPolicyDecision(assemblyOverride.Policy, assemblyOverride.Reason ?? AssemblyOverrideReason);
        }

        return new SimulationApiPolicyDecision(DefaultPolicy, DefaultReason);
    }
}
