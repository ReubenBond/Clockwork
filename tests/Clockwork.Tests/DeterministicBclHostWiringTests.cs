using Clockwork.Runtime.Shims;

namespace Clockwork.Tests;

/// <summary>
/// <para>
/// Proves the Phase 5 host wiring: <see cref="SimulationCluster{TNode}"/> registers a
/// <see cref="SimulationRuntimeEnvironment"/> for its runtime so ordinary code whose direct BCL
/// calls have been redirected to the deterministic shims observes virtual, per-node-isolated,
/// replayable time/identity/randomness while the cluster's ambient runtime is active - without any
/// dependency injection or manual service plumbing.
/// </para>
/// <para>
/// These tests call the shims directly (as instrumented code would after rewriting) from work
/// scheduled on a node's ambient-integrated queue. They also pin the production pass-through
/// contract: outside any simulation the same shim entry points fall through to the real BCL API.
/// </para>
/// </summary>
public sealed class DeterministicBclHostWiringTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(10);

    [Fact]
    public async Task NodeWorkObservesSimulatedUtcTimeThroughClockShim()
    {
        var builder = new SimulationBuilder().WithSeed(1).WithStartDateTime(Start);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        DateTime captured = default;
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => captured = DeterministicClock.GetUtcNow()));
        cluster.RunUntilIdle();

        Assert.Equal(Start.UtcDateTime, captured);
        Assert.Equal(DateTimeKind.Utc, captured.Kind);
    }

    [Fact]
    public async Task NodeWorkObservesSimulatedTimestampAndTickCountThroughShims()
    {
        var builder = new SimulationBuilder().WithSeed(1).WithStartDateTime(Start);
        var node = builder.AddNode("node-1");
        await using var cluster = builder.Build();

        long timestamp = -1;
        long tickCount64 = -1;
        node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() =>
        {
            timestamp = DeterministicClock.GetTimestamp();
            tickCount64 = DeterministicClock.GetTickCount64();
        }));
        cluster.RunUntilIdle();

        // Origin is StartDateTime, so with no time advanced both read zero elapsed - fully virtual,
        // never the host machine's real Stopwatch/Environment counters.
        Assert.Equal(0, timestamp);
        Assert.Equal(0, tickCount64);
    }

    [Fact]
    public async Task TwoNodesNeverShareSharedRandomState()
    {
        var builder = new SimulationBuilder().WithSeed(7).WithStartDateTime(Start);
        var a = builder.AddNode("node-a");
        var b = builder.AddNode("node-b");
        await using var cluster = builder.Build();

        int firstA = 0;
        int firstB = 0;
        a.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => firstA = DeterministicRandom.GetShared().Next()));
        b.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => firstB = DeterministicRandom.GetShared().Next()));
        cluster.RunUntilIdle();

        // Distinct nodes draw from independent application-domain streams, so a draw on one node does
        // not consume or perturb the other node's stream.
        Assert.NotEqual(firstA, firstB);
    }

    [Fact]
    public async Task SameSeedAndScheduleReplaysIdentityAndRandom()
    {
        static async Task<(Guid Guid, int Random)> RunOnce()
        {
            var builder = new SimulationBuilder().WithSeed(42).WithStartDateTime(Start);
            var node = builder.AddNode("node-1");
            await using var cluster = builder.Build();

            Guid guid = default;
            int random = 0;
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() =>
            {
                guid = DeterministicGuid.NewGuid();
                random = DeterministicRandom.GetShared().Next();
            }));
            cluster.RunUntilIdle();
            return (guid, random);
        }

        var first = await RunOnce();
        var second = await RunOnce();

        Assert.Equal(first.Guid, second.Guid);
        Assert.Equal(first.Random, second.Random);
        Assert.NotEqual(Guid.Empty, first.Guid);
    }

    [Fact]
    public async Task DifferentSeedsDiverge()
    {
        static async Task<Guid> RunWithSeed(int seed)
        {
            var builder = new SimulationBuilder().WithSeed(seed).WithStartDateTime(Start);
            var node = builder.AddNode("node-1");
            await using var cluster = builder.Build();

            Guid guid = default;
            node.Context.TaskQueue.Enqueue(new ScheduledActionItem(() => guid = DeterministicGuid.NewGuid()));
            cluster.RunUntilIdle();
            return guid;
        }

        Assert.NotEqual(await RunWithSeed(1), await RunWithSeed(2));
    }

    [Fact]
    public async Task ClusterRegistersAndUnregistersRuntimeEnvironment()
    {
        var builder = new SimulationBuilder().WithSeed(1).WithStartDateTime(Start);
        _ = builder.AddNode("node-1");
        var cluster = builder.Build();

        Assert.True(SimulationRuntimeServices.TryGet(cluster.RuntimeIdentity, out var env));
        Assert.Same(cluster.RuntimeEnvironment, env);

        await cluster.DisposeAsync();

        Assert.False(SimulationRuntimeServices.TryGet(cluster.RuntimeIdentity, out _));
    }

    [Fact]
    public void ShimsFallThroughToRealBclOutsideSimulation()
    {
        // No cluster/ambient runtime is active on this thread, so the shims must run the real BCL API.
        DateTime before = DateTime.UtcNow;
        DateTime shimmed = DeterministicClock.GetUtcNow();
        DateTime after = DateTime.UtcNow;

        Assert.InRange(shimmed, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.NotEqual(Guid.Empty, DeterministicGuid.NewGuid());
        Assert.NotSame(DeterministicRandom.CreateUnseeded(), DeterministicRandom.CreateUnseeded());
    }
}
