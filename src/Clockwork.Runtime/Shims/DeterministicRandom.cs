using System.ComponentModel;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// The deterministic replacements for <see cref="System.Random.Shared"/> and the <see cref="System.Random"/>
/// constructors. Instrumented code has <c>Random.Shared</c>, <c>new Random()</c>, and
/// <c>new Random(int)</c> redirected here. Outside a simulation each method reproduces the exact BCL
/// behaviour; inside a simulation with no registered environment the stream-backed methods throw
/// <see cref="SimulationServiceMissingException"/>.
/// </para>
/// <para><b>Stream lifetime and isolation semantics.</b></para>
/// <list type="bullet">
/// <item><description>
/// <see cref="GetShared"/> (<c>Random.Shared</c>): one stable <see cref="System.Random"/> per node for the
/// life of the simulation, seeded from the application seed domain. Successive draws advance that one
/// per-node stream; the instance is isolated per node so nodes never share mutable state. (Unlike the
/// real <c>Random.Shared</c> it is not thread-safe, which is safe here because a simulated node runs
/// single-threaded under the scheduler.)
/// </description></item>
/// <item><description>
/// <see cref="CreateUnseeded"/> (<c>new Random()</c>): a fresh independent <see cref="System.Random"/> per
/// construction, seeded from a per-node monotonic construction counter. The sequence a program sees is
/// reproducible under a fixed schedule, and distinct constructions never share state.
/// </description></item>
/// <item><description>
/// <see cref="CreateSeeded"/> (<c>new Random(int)</c>): the caller's explicit seed is preserved
/// exactly - a seeded <see cref="System.Random"/> is already deterministic and reproducible, so the shim does
/// not reseed it. Consequently two nodes that both call <c>new Random(42)</c> observe identical
/// sequences <em>by the caller's explicit choice</em>; this is the documented compatibility policy.
/// </description></item>
/// </list>
/// <para>
/// All simulated randomness here is drawn from the application seed domain only, so consuming it never
/// perturbs the scheduler, network, or fault-injection ("Buggify") domains.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeterministicRandom
{
    /// <summary>Deterministic replacement for <see cref="System.Random.Shared"/>.</summary>
    /// <returns>The node's shared deterministic stream when simulating; otherwise <see cref="System.Random.Shared"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static System.Random GetShared()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Random.Shared", out var env, out var node))
        {
            return System.Random.Shared;
        }

        return env.GetSharedRandom(node);
    }

    /// <summary>Deterministic replacement for the parameterless <see cref="System.Random"/> constructor.</summary>
    /// <returns>A fresh deterministic stream when simulating; otherwise <c>new Random()</c>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static System.Random CreateUnseeded()
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment("System.Random..ctor()", out var env, out var node))
        {
            return new System.Random();
        }

        return env.CreateUnseededRandom(node);
    }

    /// <summary>
    /// Deterministic replacement for the seeded <see cref="System.Random"/> constructor. The caller's
    /// seed is preserved exactly, in and out of simulation, per the documented compatibility policy.
    /// </summary>
    /// <param name="seed">The seed supplied by the caller.</param>
    /// <returns>A <see cref="System.Random"/> seeded with <paramref name="seed"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static System.Random CreateSeeded(int seed)
    {
        // An explicitly-seeded Random is already deterministic and reproducible, so it draws no ambient
        // time or randomness: the deterministic contract only requires that we not perturb the caller's
        // chosen seed. We therefore honour the seed verbatim in and out of simulation, and deliberately
        // do not require a registered environment (there is nothing irreproducible to guard against).
        return new System.Random(seed);
    }
}

