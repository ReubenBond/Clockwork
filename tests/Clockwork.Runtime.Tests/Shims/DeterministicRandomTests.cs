using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Semantic conformance tests for <see cref="DeterministicRandom"/>: shared-stream stability and
/// isolation, unseeded independence, seeded-seed preservation, same-seed replay, per-node isolation,
/// stream-domain independence, inherited/virtual API surface, active missing-context failure, and
/// inactive pass-through.
/// </summary>
public sealed class DeterministicRandomTests
{
    [Fact]
    public void SharedReturnsAStableInstanceForTheSameNode()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var same = ShimTestHarness.RunInSimulation(env, () =>
            ReferenceEquals(DeterministicRandom.GetShared(), DeterministicRandom.GetShared()));

        Assert.True(same);
    }

    [Fact]
    public void SharedStreamAdvancesAcrossDraws()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var (first, second) = ShimTestHarness.RunInSimulation(env, () =>
        {
            var shared = DeterministicRandom.GetShared();
            return (shared.Next(), shared.Next());
        });

        // Overwhelmingly likely to differ; this asserts the stream advances rather than repeating.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SameSeedAndScheduleReplaysSharedDraws()
    {
        int Draw()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () => DeterministicRandom.GetShared().Next());
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void DifferentNodesGetIsolatedSharedStreams()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var a = ShimTestHarness.RunInSimulation(env, () => DeterministicRandom.GetShared().Next(), nodeAddress: "10.0.0.1");
        var b = ShimTestHarness.RunInSimulation(env, () => DeterministicRandom.GetShared().Next(), nodeAddress: "10.0.0.2");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UnseededConstructionsAreIndependentInstances()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var distinct = ShimTestHarness.RunInSimulation(env, () =>
        {
            var r1 = DeterministicRandom.CreateUnseeded();
            var r2 = DeterministicRandom.CreateUnseeded();
            return !ReferenceEquals(r1, r2);
        });

        Assert.True(distinct);
    }

    [Fact]
    public void UnseededConstructionsProduceDifferentSequences()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var (a, b) = ShimTestHarness.RunInSimulation(env, () =>
            (DeterministicRandom.CreateUnseeded().Next(), DeterministicRandom.CreateUnseeded().Next()));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UnseededSequenceReplaysUnderAFixedSchedule()
    {
        int[] Draw()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                var first = DeterministicRandom.CreateUnseeded().Next();
                var second = DeterministicRandom.CreateUnseeded().Next();
                return new[] { first, second };
            });
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void SeededConstructionPreservesTheCallerSeedExactly()
    {
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var shimmed = ShimTestHarness.RunInSimulation(env, () => DeterministicRandom.CreateSeeded(4242).Next());
        var reference = new System.Random(4242).Next();

        Assert.Equal(reference, shimmed);
    }

    [Fact]
    public void SeededConstructionDoesNotRequireARegisteredEnvironment()
    {
        // A seeded Random is already deterministic, so it must pass through even when active with no
        // environment registered (nothing irreproducible to guard).
        ShimTestHarness.RunInSimulationWithoutEnvironment(() =>
        {
            var value = DeterministicRandom.CreateSeeded(99).Next();
            Assert.Equal(new System.Random(99).Next(), value);
        });
    }

    [Fact]
    public void InheritedAndVirtualRandomSurfaceIsDeterministic()
    {
        // Exercise a representative spread of the Random API surface (virtual/inherited members) on the
        // deterministic instance and confirm identical replay.
        object[] Draw()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                var r = DeterministicRandom.GetShared();
                var buffer = new byte[8];
                r.NextBytes(buffer);
                return new object[]
                {
                    r.Next(),
                    r.Next(100),
                    r.Next(10, 20),
                    r.NextInt64(),
                    r.NextInt64(1000),
                    r.NextInt64(5, 50),
                    r.NextSingle(),
                    r.NextDouble(),
                    Convert.ToHexString(buffer),
                };
            });
        }

        Assert.Equal(Draw(), Draw());
    }

    [Fact]
    public void ConsumingRandomDoesNotPerturbTheIdentityStream()
    {
        // Drawing application randomness must not change the GUID/identity stream: different domains.
        Guid WithoutDraw()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, DeterministicGuid.NewGuid);
        }

        Guid WithDraw()
        {
            var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());
            return ShimTestHarness.RunInSimulation(env, () =>
            {
                _ = DeterministicRandom.GetShared().Next();
                _ = DeterministicRandom.CreateUnseeded().Next();
                return DeterministicGuid.NewGuid();
            });
        }

        Assert.Equal(WithoutDraw(), WithDraw());
    }

    [Fact]
    public void ActiveSimulationWithoutRegisteredEnvironmentFailsExplicitly()
    {
        ShimTestHarness.RunInSimulationWithoutEnvironment(() =>
        {
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicRandom.GetShared());
            Assert.Throws<SimulationServiceMissingException>(() => DeterministicRandom.CreateUnseeded());
        });
    }

    [Fact]
    public void OutsideSimulationRandomShimsPassThroughToTheRealBcl()
    {
        Assert.False(Clockwork.Runtime.Execution.SimulationExecutionContext.IsActive);

        Assert.Same(System.Random.Shared, DeterministicRandom.GetShared());
        Assert.NotNull(DeterministicRandom.CreateUnseeded());
        Assert.Equal(new System.Random(7).Next(), DeterministicRandom.CreateSeeded(7).Next());
    }
}

