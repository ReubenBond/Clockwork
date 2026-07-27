namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>
/// The API family a built-in <see cref="RewriteRule"/> belongs to. Families are the unit of granular
/// include/exclude selection for the built-in deterministic BCL rule set: a caller can opt a whole
/// family in or out, but never edit individual signatures, so the shipped inventory stays coherent.
/// </summary>
public enum BuiltInRuleFamily
{
    /// <summary>
    /// Wall-clock and monotonic time: <see cref="System.DateTime"/>, <see cref="System.DateTimeOffset"/>,
    /// <see cref="System.Diagnostics.Stopwatch"/> static timestamp APIs, and
    /// <see cref="System.Environment"/> tick counters.
    /// </summary>
    Clock,

    /// <summary>Identity: <see cref="System.Guid"/> factory methods (<c>NewGuid</c>, <c>CreateVersion7</c>).</summary>
    Identity,

    /// <summary>General-purpose pseudo-randomness: <see cref="System.Random"/> shared instance and constructors.</summary>
    Random,

    /// <summary>
    /// Cryptographic randomness: the static <see cref="System.Security.Cryptography.RandomNumberGenerator"/>
    /// APIs and factories that draw operating-system entropy. Controlled to the policy shim, which rejects
    /// by default and only serves deterministic-insecure bytes under an explicit test-only opt-in.
    /// </summary>
    Crypto,
}
