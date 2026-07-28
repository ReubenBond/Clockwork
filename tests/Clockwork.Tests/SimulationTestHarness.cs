using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Shims;

namespace Clockwork.Tests;

internal static class SimulationTestHarness
{
    public static SimulationScheduler NewScheduler(
        DateTimeOffset? startDateTime = null,
        int seed = 1)
    {
        var runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, "test");
        return new SimulationScheduler(
            runtime,
            new SimulationSeedAuthority(seed),
            startDateTime ?? DateTimeOffset.UnixEpoch,
            TimeZoneInfo.Utc,
            SimulationCryptoRandomnessPolicy.Reject);
    }

    public static SingleThreadedGuard NewGuard(SimulationScheduler scheduler) =>
        new(() => scheduler.IsSimulationThread
            ? SimulationScheduler.SimulationLogicalThreadOwnerId
            : Environment.CurrentManagedThreadId);

    public static SimulationSchedulerLane NewLane(
        DateTimeOffset? startDateTime = null,
        SimulationNodeIdentity? node = null)
    {
        var scheduler = NewScheduler(startDateTime);
        return new SimulationSchedulerLane(scheduler, NewGuard(scheduler), node);
    }

    public static (SimulationClock Clock, SingleThreadedGuard Guard, SimulationNodeContext Context)
        NewNodeComponents(
            string address = "node-1",
            SimulationSchedulerLane? externalLane = null)
    {
        var scheduler = externalLane?.Scheduler ?? NewScheduler();
        var clock = new SimulationClock(scheduler);
        var guard = NewGuard(scheduler);
        var node = new SimulationNodeIdentity(address);
        var context = new SimulationNodeContext(
            clock,
            guard,
            new SimulationRandom(1),
            externalLane,
            logger: null,
            scheduler.Runtime,
            node);
        return (clock, guard, context);
    }
}
