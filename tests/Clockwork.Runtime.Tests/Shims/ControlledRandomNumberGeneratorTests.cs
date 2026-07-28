using System.Security.Cryptography;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Conformance tests for <see cref="ControlledRandomNumberGenerator"/>: reject-by-default diagnostics for
/// every controlled static, inactive-simulation rejection, and the explicit deterministic-insecure opt-in
/// (determinism, replay, and instance-generator behaviour).
/// </summary>
public sealed class ControlledRandomNumberGeneratorTests
{
    private static ShimTestHarness.TestEnvironment RejectEnvironment() =>
        ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

    private static ShimTestHarness.TestEnvironment InsecureEnvironment() =>
        ShimTestHarness.CreateEnvironment(
            ShimTestHarness.CreateClock(),
            cryptoPolicy: SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting);

    [Fact]
    public void RejectPolicyRejectsEveryControlledStaticWithAPreciseDiagnostic()
    {
        var env = RejectEnvironment();

        ShimTestHarness.RunInSimulation(env, () =>
        {
            AssertRejected(() => ControlledRandomNumberGenerator.Create(), "RandomNumberGenerator.Create");
            AssertRejected(() => ControlledRandomNumberGenerator.Create("SHA1PRNG"), "RandomNumberGenerator.Create");
            AssertRejected(() => ControlledRandomNumberGenerator.GetBytes(16), "RandomNumberGenerator.GetBytes");
            AssertRejected(() => ControlledRandomNumberGenerator.GetInt32(100), "RandomNumberGenerator.GetInt32");
            AssertRejected(() => ControlledRandomNumberGenerator.GetInt32(1, 100), "RandomNumberGenerator.GetInt32");
            AssertRejected(() => ControlledRandomNumberGenerator.GetHexString(8), "RandomNumberGenerator.GetHexString");
            AssertRejected(() => ControlledRandomNumberGenerator.GetString("abcdef", 8), "RandomNumberGenerator.GetString");
            AssertRejected(
                () =>
                {
                    Span<byte> buf = stackalloc byte[8];
                    ControlledRandomNumberGenerator.Fill(buf);
                },
                "RandomNumberGenerator.Fill");
            AssertRejected(
                () =>
                {
                    Span<char> hex = stackalloc char[8];
                    ControlledRandomNumberGenerator.GetHexString(hex);
                },
                "RandomNumberGenerator.GetHexString");
        });
    }

    [Fact]
    public void OutsideSimulationCryptoShimsRequireActiveSimulation()
    {
        Assert.False(Clockwork.Runtime.Execution.SimulationExecutionContext.IsActive);
        RandomNumberGenerator? generator = null;

        Exception? createException = Record.Exception(
            () => generator = ControlledRandomNumberGenerator.Create());
        Assert.Null(generator);
        SimulationNotActiveExceptionAssert.Equal(
            createException,
            "System.Security.Cryptography.RandomNumberGenerator.Create");

        byte[]? bytes = null;
        Exception? bytesException = Record.Exception(
            () => bytes = ControlledRandomNumberGenerator.GetBytes(16));
        Assert.Null(bytes);
        SimulationNotActiveExceptionAssert.Equal(
            bytesException,
            "System.Security.Cryptography.RandomNumberGenerator.GetBytes");

        Exception? invalidCountException = Record.Exception(
            () => ControlledRandomNumberGenerator.GetBytes(-1));
        SimulationNotActiveExceptionAssert.Equal(
            invalidCountException,
            "System.Security.Cryptography.RandomNumberGenerator.GetBytes");

        var randomInt = -1;
        Exception? intException = Record.Exception(
            () => randomInt = ControlledRandomNumberGenerator.GetInt32(1, 10));
        Assert.Equal(-1, randomInt);
        SimulationNotActiveExceptionAssert.Equal(
            intException,
            "System.Security.Cryptography.RandomNumberGenerator.GetInt32");

        string? hex = null;
        Exception? hexException = Record.Exception(
            () => hex = ControlledRandomNumberGenerator.GetHexString(8));
        Assert.Null(hex);
        SimulationNotActiveExceptionAssert.Equal(
            hexException,
            "System.Security.Cryptography.RandomNumberGenerator.GetHexString");
    }

    [Fact]
    public void InsecurePolicyProducesDeterministicBytesThatReplay()
    {
        byte[] Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => ControlledRandomNumberGenerator.GetBytes(32));
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void InsecurePolicyGetInt32StaysInRangeAndReplays()
    {
        int Draw()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () => ControlledRandomNumberGenerator.GetInt32(10, 20));
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
            return ShimTestHarness.RunInSimulation(env, () => ControlledRandomNumberGenerator.GetHexString(16, lowercase: true));
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
            return ShimTestHarness.RunInSimulation(env, () => ControlledRandomNumberGenerator.GetString(Choices, 12));
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
                using var rng = ControlledRandomNumberGenerator.Create();
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
            using var rng = ControlledRandomNumberGenerator.Create();
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
                (ControlledRandom.GetShared().Next(), ControlledGuid.NewGuid()));
        }

        (int Random, Guid Identity) WithCrypto()
        {
            var env = InsecureEnvironment();
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                _ = ControlledRandomNumberGenerator.GetBytes(16);
                return (ControlledRandom.GetShared().Next(), ControlledGuid.NewGuid());
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
            () => Record.Exception(() => _ = ControlledRandomNumberGenerator.GetBytes(count)));

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
            () => Record.Exception(() => _ = ControlledRandomNumberGenerator.GetInt32(toExclusive)));

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
                () => _ = ControlledRandomNumberGenerator.GetInt32(fromInclusive, toExclusive)));

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
                    ControlledRandomNumberGenerator.Create("RandomNumberGenerator");
                var deterministic = Assert.IsType<SimulationInsecureRandomNumberGenerator>(generator);
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
            () => ControlledRandomNumberGenerator.Create(UnknownName));

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
                ControlledRandomNumberGenerator.GetBytes(0),
                ControlledRandomNumberGenerator.GetBytes(24)));

        var baselineEnvironment = InsecureEnvironment();
        byte[] baseline = ShimTestHarness.RunInSimulation(
            baselineEnvironment,
            () => ControlledRandomNumberGenerator.GetBytes(24));

        Assert.Empty(observation.Empty);
        Assert.Equal(baseline, observation.Following);
        Assert.Contains(observation.Following, static value => value != 0);
    }
}
