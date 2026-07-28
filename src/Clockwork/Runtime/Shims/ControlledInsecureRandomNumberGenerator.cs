using System.ComponentModel;
using System.Security.Cryptography;
using Clockwork.Runtime.Execution;

namespace Clockwork.Runtime.Shims;

/// <summary>
/// <para>
/// A <see cref="RandomNumberGenerator"/> that produces deterministic, <b>non-cryptographic</b> bytes
/// from a simulation's insecure crypto stream. It exists only to satisfy the explicit, test-only
/// <see cref="SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting"/> policy so that code
/// paths calling <c>RandomNumberGenerator.Create()</c> can run reproducibly inside a simulation.
/// </para>
/// <para>
/// <b>This is not secure.</b> The bytes it yields are reproducible by design and must never be used
/// for keys, nonces, tokens, or any real security decision. It is unreachable in production because a
/// production process is never a simulation host and therefore never has an environment with this
/// policy registered.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ControlledInsecureRandomNumberGenerator : RandomNumberGenerator
{
    private readonly ISimulationRuntimeEnvironment _environment;
    private readonly SimulationNodeIdentity? _node;

    /// <summary>Initializes a new instance of the <see cref="ControlledInsecureRandomNumberGenerator"/> class.</summary>
    /// <param name="environment">The environment supplying deterministic insecure bytes.</param>
    /// <param name="node">The active node identity, or <see langword="null"/> for cluster-level execution.</param>
    public ControlledInsecureRandomNumberGenerator(ISimulationRuntimeEnvironment environment, SimulationNodeIdentity? node)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
        _node = node;
    }

    /// <inheritdoc/>
    public override void GetBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _environment.FillInsecureCryptoBytes(_node, data);
    }

    /// <inheritdoc/>
    public override void GetBytes(Span<byte> data) => _environment.FillInsecureCryptoBytes(_node, data);

    /// <inheritdoc/>
    public override void GetNonZeroBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        GetNonZeroBytes(data.AsSpan());
    }

    /// <inheritdoc/>
    public override void GetNonZeroBytes(Span<byte> data)
    {
        for (var i = 0; i < data.Length;)
        {
            _environment.FillInsecureCryptoBytes(_node, data.Slice(i, 1));
            if (data[i] != 0)
            {
                i++;
            }
        }
    }
}
