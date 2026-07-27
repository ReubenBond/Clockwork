using System.ComponentModel;
using System.Security.Cryptography;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// The policy shims for the static cryptographic-randomness APIs on
/// <see cref="RandomNumberGenerator"/>. These draw operating-system entropy that cannot be reproduced
/// from a seed, so a simulation must not let them run silently. Instrumented code has the static
/// <c>RandomNumberGenerator</c> members redirected here (including the two <see cref="RandomNumberGenerator.Create()"/>
/// factories, which are the entropy-bearing constructors of concrete algorithm instances).
/// </para>
/// <para>
/// Outside a simulation every method calls the real BCL API unchanged - production security semantics
/// are never altered. Inside a simulation the behaviour follows the environment's
/// <see cref="ISimulationRuntimeEnvironment.CryptoPolicy"/>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="SimulationCryptoRandomnessPolicy.Reject"/> (the default) throws
/// <see cref="SimulationRejectedCallException"/> naming the exact API - the precise rejected-call
/// diagnostic.
/// </description></item>
/// <item><description>
/// <see cref="SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting"/> serves deterministic
/// <b>non-cryptographic</b> bytes (see <see cref="InsecureDeterministicRandomNumberGenerator"/>). This
/// is an explicit, test-only opt-in that a simulation host must configure; it never affects
/// production, which is not a simulation host.
/// </description></item>
/// </list>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DeterministicCryptoRandom
{
    /// <summary>Policy shim for <see cref="RandomNumberGenerator.Create()"/>.</summary>
    /// <returns>A real or deterministic-insecure generator; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RandomNumberGenerator Create()
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.Create", out var env, out var node))
        {
            return RandomNumberGenerator.Create();
        }

        return new InsecureDeterministicRandomNumberGenerator(env, node);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.Create(string)"/>.</summary>
    /// <param name="name">The algorithm name.</param>
    /// <returns>A real or deterministic-insecure generator; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static RandomNumberGenerator? Create(string name)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.Create", out var env, out var node))
        {
#pragma warning disable SYSLIB0045 // Named crypto factory is obsolete; the shim must faithfully forward it.
            return RandomNumberGenerator.Create(name);
#pragma warning restore SYSLIB0045
        }

        return new InsecureDeterministicRandomNumberGenerator(env, node);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.Fill(Span{byte})"/>.</summary>
    /// <param name="data">The buffer to fill.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Fill(Span<byte> data)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.Fill", out var env, out var node))
        {
            RandomNumberGenerator.Fill(data);
            return;
        }

        env.FillInsecureCryptoBytes(node, data);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetBytes(int)"/>.</summary>
    /// <param name="count">The number of bytes to produce.</param>
    /// <returns>A buffer of random or deterministic-insecure bytes; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static byte[] GetBytes(int count)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetBytes", out var env, out var node))
        {
            return RandomNumberGenerator.GetBytes(count);
        }

        var buffer = new byte[count];
        env.FillInsecureCryptoBytes(node, buffer);
        return buffer;
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetInt32(int)"/>.</summary>
    /// <param name="toExclusive">The exclusive upper bound.</param>
    /// <returns>A value in <c>[0, toExclusive)</c>; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int GetInt32(int toExclusive)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetInt32", out var env, out var node))
        {
            return RandomNumberGenerator.GetInt32(toExclusive);
        }

        return GetInt32(0, toExclusive, env, node);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetInt32(int, int)"/>.</summary>
    /// <param name="fromInclusive">The inclusive lower bound.</param>
    /// <param name="toExclusive">The exclusive upper bound.</param>
    /// <returns>A value in <c>[fromInclusive, toExclusive)</c>; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static int GetInt32(int fromInclusive, int toExclusive)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetInt32", out var env, out var node))
        {
            return RandomNumberGenerator.GetInt32(fromInclusive, toExclusive);
        }

        return GetInt32(fromInclusive, toExclusive, env, node);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetHexString(Span{char}, bool)"/>.</summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="lowercase">Whether to emit lowercase hex.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void GetHexString(Span<char> destination, bool lowercase = false)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetHexString", out var env, out var node))
        {
            RandomNumberGenerator.GetHexString(destination, lowercase);
            return;
        }

        FillHex(destination, lowercase, env, node);
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetHexString(int, bool)"/>.</summary>
    /// <param name="stringLength">The number of hex characters to produce.</param>
    /// <param name="lowercase">Whether to emit lowercase hex.</param>
    /// <returns>A hex string; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string GetHexString(int stringLength, bool lowercase = false)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetHexString", out var env, out var node))
        {
            return RandomNumberGenerator.GetHexString(stringLength, lowercase);
        }

        if (stringLength == 0)
        {
            return string.Empty;
        }

        return string.Create(stringLength, (env, node, lowercase), static (span, state) =>
            FillHex(span, state.lowercase, state.env, state.node));
    }

    /// <summary>Policy shim for <see cref="RandomNumberGenerator.GetString(ReadOnlySpan{char}, int)"/>.</summary>
    /// <param name="choices">The allowed characters.</param>
    /// <param name="length">The number of characters to produce.</param>
    /// <returns>A string drawn from <paramref name="choices"/>; rejects under the default policy.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string GetString(ReadOnlySpan<char> choices, int length)
    {
        if (!TryEnterControlled("System.Security.Cryptography.RandomNumberGenerator.GetString", out var env, out var node))
        {
            return RandomNumberGenerator.GetString(choices, length);
        }

        if (choices.IsEmpty || length < 0)
        {
            // Defer to the BCL's own argument validation for the exact exception shape.
            return RandomNumberGenerator.GetString(choices, length);
        }

        if (length == 0)
        {
            return string.Empty;
        }

        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = choices[GetInt32(0, choices.Length, env, node)];
        }

        return new string(result);
    }

    /// <summary>
    /// Resolves the controlled crypto path: returns <see langword="false"/> outside a simulation (run
    /// the real BCL API); throws <see cref="SimulationRejectedCallException"/> under the reject policy;
    /// returns <see langword="true"/> with the environment under the insecure test policy.
    /// </summary>
    private static bool TryEnterControlled(
        string apiName,
        out ISimulationRuntimeEnvironment environment,
        out SimulationNodeIdentity? node)
    {
        if (!SimulationRuntimeDispatch.TryGetEnvironment(apiName, out environment, out node))
        {
            return false;
        }

        if (environment.CryptoPolicy == SimulationCryptoRandomnessPolicy.Reject)
        {
            var (runtime, _, _) = SimulationRuntimeDispatch.RequireEnvironment(apiName);
            throw new SimulationRejectedCallException(
                runtime,
                apiName,
                "cryptographic randomness draws operating-system entropy that cannot be reproduced " +
                "deterministically in a simulation.");
        }

        return true;
    }

    private static int GetInt32(int fromInclusive, int toExclusive, ISimulationRuntimeEnvironment environment, SimulationNodeIdentity? node)
    {
        if (fromInclusive >= toExclusive)
        {
            throw new ArgumentException("fromInclusive must be less than toExclusive.", nameof(fromInclusive));
        }

        var range = (uint)(toExclusive - fromInclusive);
        return fromInclusive + (int)(NextUInt32(environment, node) % range);
    }

    private static void FillHex(Span<char> destination, bool lowercase, ISimulationRuntimeEnvironment environment, SimulationNodeIdentity? node)
    {
        const string Upper = "0123456789ABCDEF";
        const string Lower = "0123456789abcdef";
        var alphabet = lowercase ? Lower : Upper;
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = alphabet[(int)(NextUInt32(environment, node) & 0x0F)];
        }
    }

    private static uint NextUInt32(ISimulationRuntimeEnvironment environment, SimulationNodeIdentity? node)
    {
        Span<byte> bytes = stackalloc byte[4];
        environment.FillInsecureCryptoBytes(node, bytes);
        return (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24));
    }
}
