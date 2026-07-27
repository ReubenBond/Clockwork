using System.Security.Cryptography;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Conformance tests for <see cref="DeterministicCryptoRandom"/>: reject-by-default diagnostics for
/// every controlled static, inactive pass-through, and the explicit deterministic-insecure opt-in
/// (determinism, replay, and instance-generator behaviour).
/// </summary>
public sealed class DeterministicCryptoRandomTests
{
    private static SimulationRuntimeEnvironment RejectEnvironment() =>
        ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

    private static SimulationRuntimeEnvironment InsecureEnvironment() =>
        ShimTestHarness.CreateEnvironment(
            ShimTestHarness.CreateClock(),
            cryptoPolicy: SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting);

    [Fact]
    public void RejectPolicyRejectsEveryControlledStaticWithAPreciseDiagnostic()
    {
        var env = RejectEnvironment();

        ShimTestHarness.RunInSimulation(env, () =>
        {
            AssertRejected(() => DeterministicCryptoRandom.Create(), "RandomNumberGenerator.Create");
            AssertRejected(() => DeterministicCryptoRandom.Create("SHA1PRNG"), "RandomNumberGenerator.Create");
            AssertRejected(() => DeterministicCryptoRandom.GetBytes(16), "RandomNumberGenerator.GetBytes");
            AssertRejected(() => DeterministicCryptoRandom.GetInt32(100), "RandomNumberGenerator.GetInt32");
            AssertRejected(() => DeterministicCryptoRandom.GetInt32(1, 100), "RandomNumberGenerator.GetInt32");
            AssertRejected(() => DeterministicCryptoRandom.GetHexString(8), "RandomNumberGenerator.GetHexString");
            AssertRejected(() => DeterministicCryptoRandom.GetString("abcdef", 8), "RandomNumberGenerator.GetString");
            AssertRejected(
                () =>
                {
                    Span<byte> buf = stackalloc byte[8];
                    DeterministicCryptoRandom.Fill(buf);
                },
                "RandomNumberGenerator.Fill");
            AssertRejected(
                () =>
                {
                    Span<char> hex = stackalloc char[8];
                    DeterministicCryptoRandom.GetHexString(hex);
                },
                "RandomNumberGenerator.GetHexString");
        });
    }

    [Fact]
    public void OutsideSimulationCryptoShimsProduceRealRandomness()
    {
        Assert.False(Clockwork.Runtime.Execution.SimulationExecutionContext.IsActive);

        using var rng = DeterministicCryptoRandom.Create();
        Assert.NotNull(rng);

        var bytes = DeterministicCryptoRandom.GetBytes(16);
        Assert.Equal(16, bytes.Length);
        Assert.InRange(DeterministicCryptoRandom.GetInt32(1, 10), 1, 9);
        Assert.Equal(8, DeterministicCryptoRandom.GetHexString(8).Length);
    }

    [Fact]
    public void InsecurePolicyProducesDeterministicBytesThatReplay()
    {
        byte[] Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => DeterministicCryptoRandom.GetBytes(32));
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void InsecurePolicyGetInt32StaysInRangeAndReplays()
    {
        int Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => DeterministicCryptoRandom.GetInt32(10, 20));
        }

        var value = Draw();
        Assert.InRange(value, 10, 19);
        Assert.Equal(value, Draw());
    }

    [Fact]
    public void InsecurePolicyHexStringIsDeterministicAndWellFormed()
    {
        string Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => DeterministicCryptoRandom.GetHexString(16, lowercase: true));
        }

        var hex = Draw();
        Assert.Equal(16, hex.Length);
        Assert.All(hex, c => Assert.Contains(c, "0123456789abcdef"));
        Assert.Equal(hex, Draw());
    }

    [Fact]
    public void InsecurePolicyGetStringDrawsOnlyFromChoices()
    {
        const string Choices = "XYZ";

        string Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => DeterministicCryptoRandom.GetString(Choices, 12));
        }

        var s = Draw();
        Assert.Equal(12, s.Length);
        Assert.All(s, c => Assert.Contains(c, Choices));
        Assert.Equal(s, Draw());
    }

    [Fact]
    public void InsecurePolicyCreateReturnsADeterministicInstanceGenerator()
    {
        byte[] Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                using var rng = DeterministicCryptoRandom.Create();
                var buffer = new byte[24];
                rng.GetBytes(buffer);
                return buffer;
            });
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void InsecureInstanceGeneratorGetNonZeroBytesHasNoZeros()
    {
        var env = InsecureEnvironment();

        var bytes = ShimTestHarness.RunInSimulation(env, () =>
        {
            using var rng = DeterministicCryptoRandom.Create();
            var buffer = new byte[64];
            rng.GetNonZeroBytes(buffer);
            return buffer;
        });

        Assert.DoesNotContain((byte)0, bytes);
    }

    [Fact]
    public void CryptoConsumptionDoesNotPerturbApplicationRandomOrIdentityStreams()
    {
        (int Random, Guid Identity) WithoutCrypto()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () =>
                (DeterministicRandom.GetShared().Next(), DeterministicGuid.NewGuid()));
        }

        (int Random, Guid Identity) WithCrypto()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                _ = DeterministicCryptoRandom.GetBytes(16);
                return (DeterministicRandom.GetShared().Next(), DeterministicGuid.NewGuid());
            });
        }

        Assert.Equal(WithoutCrypto(), WithCrypto());
    }

    private static void AssertRejected(Action action, string expectedApiFragment)
    {
        var ex = Assert.Throws<SimulationRejectedCallException>(action);
        Assert.Contains(expectedApiFragment, ex.ApiName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetBytesNegativeCountMatchesBclExceptionShape(int count)
    {
        Exception? bclError = Record.Exception(() => _ = RandomNumberGenerator.GetBytes(count));
        var env = InsecureEnvironment();
        Exception? controlledError = ShimTestHarness.RunInSimulation(
            env,
            () => Record.Exception(() => _ = DeterministicCryptoRandom.GetBytes(count)));

        AssertExceptionShape<ArgumentOutOfRangeException>(bclError, controlledError, "count");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void GetInt32SingleBoundRejectsNonPositiveValuesLikeBcl(int toExclusive)
    {
        Exception? bclError = Record.Exception(() => _ = RandomNumberGenerator.GetInt32(toExclusive));
        var env = InsecureEnvironment();
        Exception? controlledError = ShimTestHarness.RunInSimulation(
            env,
            () => Record.Exception(() => _ = DeterministicCryptoRandom.GetInt32(toExclusive)));

        AssertExceptionShape<ArgumentOutOfRangeException>(bclError, controlledError, "toExclusive");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(int.MaxValue, int.MinValue)]
    public void GetInt32RangeRejectsEqualOrDescendingBoundsLikeBcl(int fromInclusive, int toExclusive)
    {
        Exception? bclError = Record.Exception(
            () => _ = RandomNumberGenerator.GetInt32(fromInclusive, toExclusive));
        var env = InsecureEnvironment();
        Exception? controlledError = ShimTestHarness.RunInSimulation(
            env,
            () => Record.Exception(
                () => _ = DeterministicCryptoRandom.GetInt32(fromInclusive, toExclusive)));

        AssertExceptionShape<ArgumentException>(bclError, controlledError, expectedParamName: null);
    }

    [Fact]
    public void NamedFactoryReturnsDeterministicGeneratorForKnownBclName()
    {
#pragma warning disable SYSLIB0045 // Verify the obsolete named BCL factory contract mirrored by the shim.
        using RandomNumberGenerator? bclGenerator = RandomNumberGenerator.Create("RandomNumberGenerator");
#pragma warning restore SYSLIB0045
        Assert.NotNull(bclGenerator);

        byte[] Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                using RandomNumberGenerator? generator =
                    DeterministicCryptoRandom.Create("RandomNumberGenerator");
                var deterministic = Assert.IsType<InsecureDeterministicRandomNumberGenerator>(generator);
                var bytes = new byte[24];
                deterministic.GetBytes(bytes);
                return bytes;
            });
        }

        byte[] first = Draw();
        byte[] replay = Draw();

        Assert.Equal(24, first.Length);
        Assert.Equal(first, replay);
        Assert.Contains(first, static value => value != 0);
    }

    [Fact]
    public void NamedFactoryReturnsNullForUnknownBclName()
    {
        const string UnknownName =
            "Clockwork.Tests.Unknown.RandomNumberGenerator.7f9c7706-3f19-42a8-b8f2-20af41a52d68";

#pragma warning disable SYSLIB0045 // Verify the obsolete named BCL factory contract mirrored by the shim.
        using RandomNumberGenerator? bclGenerator = RandomNumberGenerator.Create(UnknownName);
#pragma warning restore SYSLIB0045
        Assert.Null(bclGenerator);

        var env = InsecureEnvironment();
        using RandomNumberGenerator? controlledGenerator = ShimTestHarness.RunInSimulation(
            env,
            () => DeterministicCryptoRandom.Create(UnknownName));

        Assert.Null(controlledGenerator);
    }

    private static void AssertExceptionShape<TException>(
        Exception? bclError,
        Exception? controlledError,
        string? expectedParamName)
        where TException : ArgumentException
    {
        var bclException = Assert.IsType<TException>(bclError);
        Assert.Equal(expectedParamName, bclException.ParamName);

        var controlledException = Assert.IsType<TException>(controlledError);
        Assert.Equal(expectedParamName, controlledException.ParamName);
    }

    [Fact]
    public void GetBytesZeroCountReturnsEmptyWithoutConsumingCryptoState()
    {
        var withBoundaryEnvironment = InsecureEnvironment();
        (byte[] Empty, byte[] Following) observation = ShimTestHarness.RunInSimulation(
            withBoundaryEnvironment,
            () => (
                DeterministicCryptoRandom.GetBytes(0),
                DeterministicCryptoRandom.GetBytes(24)));

        var baselineEnvironment = InsecureEnvironment();
        byte[] baseline = ShimTestHarness.RunInSimulation(
            baselineEnvironment,
            () => DeterministicCryptoRandom.GetBytes(24));

        Assert.Empty(observation.Empty);
        Assert.Equal(baseline, observation.Following);
        Assert.Contains(observation.Following, static value => value != 0);
    }
}
