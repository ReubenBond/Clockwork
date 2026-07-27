using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// Thrown by a deterministic BCL shim when a simulation is active and the targeted API is
/// <em>rejected</em> under the active environment's policy - most importantly the cryptographic
/// randomness APIs (<see cref="System.Security.Cryptography.RandomNumberGenerator"/> statics), which
/// draw operating-system entropy that cannot be reproduced. By default a simulation rejects these
/// calls loudly rather than silently substituting insecure bytes; a host may opt into an explicitly
/// test-only deterministic model (see <see cref="SimulationCryptoRandomnessPolicy"/>), but production
/// code - which is never a simulation host - is unaffected.
/// </summary>
public sealed class SimulationRejectedCallException : SimulationShimException
{
    /// <summary>Initializes a new instance of the <see cref="SimulationRejectedCallException"/> class.</summary>
    /// <param name="runtime">The active simulation runtime under which the call was rejected.</param>
    /// <param name="apiName">The fully-qualified name of the rejected API.</param>
    /// <param name="reason">A human-readable reason the API is rejected.</param>
    public SimulationRejectedCallException(SimulationRuntimeIdentity runtime, string apiName, string reason)
        : base(BuildMessage(runtime, apiName, reason))
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Runtime = runtime;
        ApiName = apiName;
        Reason = reason;
    }

    /// <summary>Gets the active simulation runtime under which the call was rejected.</summary>
    public SimulationRuntimeIdentity Runtime { get; }

    /// <summary>Gets the fully-qualified name of the rejected API.</summary>
    public string ApiName { get; }

    /// <summary>Gets a human-readable reason the API is rejected.</summary>
    public string Reason { get; }

    private static string BuildMessage(SimulationRuntimeIdentity runtime, string apiName, string reason)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return
            $"The call to '{apiName}' is rejected inside simulation runtime '{runtime.Id}' " +
            $"(seed {runtime.Seed}): {reason} To use a deterministic, explicitly insecure model for " +
            "test scenarios, configure the runtime environment's crypto policy to " +
            $"'{nameof(SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting)}'.";
    }
}
