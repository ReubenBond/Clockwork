using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Semantic conformance tests for <see cref="DeterministicGuid"/>: RFC variant/version shape, V7
/// timestamp extraction, determinism/replay, per-node isolation, independence from the application
/// random streams, active missing-context failure, and inactive pass-through.
/// </summary>
public sealed class DeterministicGuidTests
{
    [Fact]
    public void NewGuidHasVersion4AndRfcVariant()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
        var guid = ShimTestHarness.RunInSimulation(env, DeterministicGuid.NewGuid);

        Assert.Equal(4, guid.Version);
        AssertRfcVariant(guid);
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public void CreateVersion7HasVersion7AndRfcVariant()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
        var guid = ShimTestHarness.RunInSimulation(env, DeterministicGuid.CreateVersion7);

        Assert.Equal(7, guid.Version);
        AssertRfcVariant(guid);
    }

    [Fact]
    public void CreateVersion7EmbedsTheVirtualUnixMillisecondTimestamp()
    {
        var instant = new DateTimeOffset(2031, 7, 8, 9, 10, 11, 123, TimeSpan.Zero);
        var clock = ShimTestHarness.CreateClock(instant);
        var env = ShimTestHarness.CreateEnvironment(clock);

        var guid = ShimTestHarness.RunInSimulation(env, DeterministicGuid.CreateVersion7);

        Assert.Equal(instant.ToUnixTimeMilliseconds(), ExtractVersion7UnixMs(guid));
    }

    [Fact]
    public void CreateVersion7WithExplicitTimestampEmbedsThatTimestamp()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
        var explicitTs = new DateTimeOffset(2040, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

        var guid = ShimTestHarness.RunInSimulation(env, () => DeterministicGuid.CreateVersion7(explicitTs));

        Assert.Equal(7, guid.Version);
        Assert.Equal(explicitTs.ToUnixTimeMilliseconds(), ExtractVersion7UnixMs(guid));
    }

    [Fact]
    public void SameSeedAndScheduleReplaysIdenticalGuids()
    {
        Guid Run()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, DeterministicGuid.NewGuid);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void DifferentNodesProduceDifferentGuidStreams()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var nodeA = ShimTestHarness.RunInSimulation(env, DeterministicGuid.NewGuid, nodeAddress: "10.0.0.1");
        var nodeB = ShimTestHarness.RunInSimulation(env, DeterministicGuid.NewGuid, nodeAddress: "10.0.0.2");

        Assert.NotEqual(nodeA, nodeB);
    }

    [Fact]
    public void RepeatedNewGuidCallsAdvanceTheIdentityStream()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var (first, second) = ShimTestHarness.RunInSimulation(env, () =>
            (DeterministicGuid.NewGuid(), DeterministicGuid.NewGuid()));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void GuidGenerationDoesNotPerturbTheApplicationRandomStream()
    {
        // Draw a shared-random value with and without an intervening GUID generation; the identity
        // stream is independent, so the application draw must be unaffected.
        int WithoutGuid()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () => env.GetSharedRandom(null!).Next());
        }

        int WithGuid()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                _ = DeterministicGuid.NewGuid();
                _ = DeterministicGuid.CreateVersion7();
                return env.GetSharedRandom(null!).Next();
            });
        }

        Assert.Equal(WithoutGuid(), WithGuid());
    }

    [Fact]
    public void ActiveSimulationWithoutRegisteredEnvironmentFailsExplicitly()
    {
        ShimTestHarness.RunInSimulationWithoutEnvironment(() =>
        {
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicGuid.NewGuid());
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicGuid.CreateVersion7());
        });
    }

    [Fact]
    public void OutsideSimulationGuidShimsPassThroughToTheRealBcl()
    {
        Assert.False(Clockwork.Runtime.Execution.SimulationExecutionContext.IsActive);

        Assert.Equal(4, DeterministicGuid.NewGuid().Version);
        Assert.Equal(7, DeterministicGuid.CreateVersion7().Version);
        Assert.NotEqual(DeterministicGuid.NewGuid(), DeterministicGuid.NewGuid());
    }

    private static void AssertRfcVariant(Guid guid)
    {
        // RFC 4122 variant: the two most-significant bits of byte 8 (big-endian) are 1 0.
        var bytes = guid.ToByteArray(bigEndian: true);
        Assert.Equal(0x80, bytes[8] & 0xC0);
    }

    private static long ExtractVersion7UnixMs(Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        long ms = 0;
        for (var i = 0; i < 6; i++)
        {
            ms = (ms << 8) | bytes[i];
        }

        return ms;
    }
}
