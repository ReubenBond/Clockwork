namespace Clockwork.Runtime.Shims;

/// <summary>
/// Controls how a simulation runtime environment treats cryptographic-randomness APIs
/// (the <see cref="System.Security.Cryptography.RandomNumberGenerator"/> statics and any factory that
/// would otherwise obtain operating-system entropy) while a simulation is active.
/// </summary>
/// <remarks>
/// <para>
/// The default is <see cref="Reject"/>: obtaining OS entropy inside a deterministic simulation cannot
/// be reproduced from a seed, so the call fails loudly with a precise diagnostic rather than silently
/// producing an irreproducible result. This never affects production code, which is not a simulation
/// host and therefore never has an environment registered.
/// </para>
/// <para>
/// <see cref="DeterministicInsecureForTesting"/> is an explicit, unmistakably named opt-in for test
/// scenarios that need cryptographic APIs to <em>run</em> deterministically (e.g. exercising a code
/// path that calls <see cref="System.Security.Cryptography.RandomNumberGenerator.GetBytes(int)"/>).
/// It substitutes deterministic, <b>non-cryptographic</b> bytes and must never be used to make
/// security decisions. Because it can only be enabled by the simulation host on a registered
/// environment, production security semantics are never changed.
/// </para>
/// </remarks>
public enum SimulationCryptoRandomnessPolicy
{
    /// <summary>
    /// Reject cryptographic-randomness calls while a simulation is active, throwing
    /// <see cref="SimulationRejectedCallException"/>. This is the strict, safe default.
    /// </summary>
    Reject,

    /// <summary>
    /// Serve cryptographic-randomness calls with deterministic, <b>non-cryptographic</b> bytes. This
    /// is an explicit, test-only opt-in and must never be relied on for real security.
    /// </summary>
    DeterministicInsecureForTesting,
}
