using System.Threading.Tasks;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tests.Tasks;
using Clockwork.Shims.System.Runtime.CompilerServices;

namespace Clockwork.Runtime.Tests.Racing;

/// <summary>
/// The race detector's retained state is bounded by the work in flight, not by the work done: a chain of continuations
/// and a <c>Parallel.ForAsync</c> over many elements each keep every clock at the same size whatever their length,
/// and drop each operation's state when it terminates, while a race at the end of the run is still reported.
/// </summary>
public sealed class RaceTrackerBoundsTests
{
    private const string Shared = "Fx.State::Shared";
    private const string Counter = "Fx.State::Counter";

    public static TheoryData<string> Chains => ["yield", "await-own-completion", "delay"];

    [Theory]
    [MemberData(nameof(Chains))]
    public void ContinuationChainKeepsClocksBoundedAndReportsARaceAtItsEnd(string chain)
    {
        RaceTrackerStatistics shortRun = RunChain(chain, links: 100, out RaceReport? shortRace);
        RaceTrackerStatistics longRun = RunChain(chain, links: 10_000, out RaceReport? longRace);

        AssertRacesWithTheEarlyRead(shortRace);
        AssertRacesWithTheEarlyRead(longRace);
        Assert.Equal(shortRun, longRun);
        Assert.Equal(0, longRun.Operations);

        // A link whose first event precedes its acquire of the previous link's completion starts a strand rather than
        // joining the previous link's, so the links of such a chain alternate between two strands.
        Assert.InRange(longRun.LargestClock, 1, 2);
    }

    [Fact]
    public void ParallelForAsyncKeepsClocksBoundedAndReportsARaceOnItsLastIteration()
    {
        RaceTrackerStatistics shortRun = RunForAsync(iterations: 100, out RaceReport? shortRace);
        RaceTrackerStatistics longRun = RunForAsync(iterations: 10_000, out RaceReport? longRace);

        AssertRacesWithTheEarlyRead(shortRace);
        AssertRacesWithTheEarlyRead(longRace);
        Assert.Equal(shortRun, longRun);
        Assert.Equal(0, longRun.Operations);

        // One strand per worker, which the simulated processor count bounds, and the caller's.
        Assert.InRange(longRun.LargestClock, 1, SimulationRuntimeEnvironment.DefaultProcessorCount + 1);
    }

    private static void AssertRacesWithTheEarlyRead(RaceReport? race)
    {
        Assert.NotNull(race);
        Assert.Equal(("reader", RaceAccessKind.Read), (race.FirstAccess.Source.Method, race.FirstAccess.Kind));
        Assert.Equal(RaceAccessKind.Write, race.SecondAccess.Kind);
        Assert.Equal(Shared, race.SecondAccess.Location.Member);
    }

    // Each link reads the shared location and writes the chain's own counter, then schedules the next link: through a
    // yield, through an await of a task the link completes after registering the continuation, or through a virtual
    // delay whose timer callback, a root operation, completes the awaited task. The last link writes the shared
    // location, which an operation started alongside the chain read once, unordered with every link.
    private static RaceTrackerStatistics RunChain(string chain, int links, out RaceReport? race)
    {
        using var host = new SimulationSchedulerTestHost();
        SimulationScheduler scheduler = host.Scheduler;
        int completed = 0;

        void Link()
        {
            RaceInstrumentation.ReadStatic(Shared, "link", 1, null, -1);
            RaceInstrumentation.WriteStatic(Counter, "link", 2, null, -1);
            if (++completed == links)
            {
                RaceInstrumentation.WriteStatic(Shared, "link", 3, null, -1);
                return;
            }

            switch (chain)
            {
                case "yield":
                    new ControlledYieldAwaitable().GetAwaiter().UnsafeOnCompleted(Link);
                    break;
                case "await-own-completion":
                    var next = new TaskCompletionSource();
                    var awaiter = new ControlledTaskAwaiter(next.Task);
                    awaiter.UnsafeOnCompleted(() =>
                    {
                        awaiter.GetResult();
                        Link();
                    });
                    ControlledTaskCompletionSource.SetResult(next);
                    break;
                default:
                    var delay = new ControlledTaskAwaiter(ControlledTask.Delay(1));
                    delay.UnsafeOnCompleted(() =>
                    {
                        delay.GetResult();
                        Link();
                    });
                    break;
            }
        }

        TaskTestHarness.RunInSimulation(host, () =>
        {
            scheduler.Schedule("chain", Link);
            scheduler.Schedule("reader", () => RaceInstrumentation.ReadStatic(Shared, "reader", 4, null, -1));
            scheduler.Drain(TestContext.Current.CancellationToken);
        });

        Assert.Equal(links, completed);
        race = scheduler.FirstRace;
        return scheduler.RaceTrackerStatistics;
    }

    // Every body reads the shared location and writes its own array element. The last iteration's body also writes the
    // shared location, which an operation started alongside the loop read once, unordered with the loop.
    private static RaceTrackerStatistics RunForAsync(int iterations, out RaceReport? race)
    {
        using var host = new SimulationSchedulerTestHost();
        SimulationScheduler scheduler = host.Scheduler;
        int[] results = new int[iterations];
        Task? loop = null;

        TaskTestHarness.RunInSimulation(host, () =>
        {
            scheduler.Schedule("loop", () =>
            {
                loop = ControlledParallel.ForAsync(0, iterations, (index, _) =>
                {
                    RaceInstrumentation.ReadStatic(Shared, "body", 1, null, -1);
                    RaceInstrumentation.WriteArray(results, index, "body", 2, null, -1);
                    if (index == iterations - 1)
                    {
                        RaceInstrumentation.WriteStatic(Shared, "body", 3, null, -1);
                    }

                    return ValueTask.CompletedTask;
                });
                ControlledTask.Wait(loop);
            });
            scheduler.Schedule("reader", () => RaceInstrumentation.ReadStatic(Shared, "reader", 4, null, -1));
            scheduler.Drain(TestContext.Current.CancellationToken);
        });

        Assert.True(loop!.IsCompletedSuccessfully);
        race = scheduler.FirstRace;
        return scheduler.RaceTrackerStatistics;
    }
}
