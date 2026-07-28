namespace Clockwork;

/// <summary>
/// Controls how cryptographic-randomness APIs behave while a simulation is active.
/// </summary>
public enum CryptoRandomnessPolicy
{
    /// <summary>Reject operating-system entropy calls so execution remains reproducible.</summary>
    Reject,

    /// <summary>
    /// Return deterministic, non-cryptographic bytes for test scenarios which must execute crypto APIs.
    /// </summary>
    DeterministicInsecureForTesting,
}

internal static class CryptoRandomnessPolicyMapping
{
    public static Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy ToRuntimePolicy(
        this CryptoRandomnessPolicy policy) =>
        policy switch
        {
            CryptoRandomnessPolicy.Reject =>
                Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.Reject,
            CryptoRandomnessPolicy.DeterministicInsecureForTesting =>
                Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unrecognized crypto randomness policy."),
        };
}
