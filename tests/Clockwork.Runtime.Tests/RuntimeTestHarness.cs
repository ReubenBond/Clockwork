using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Tests;

internal static class RuntimeTestHarness
{
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static SimulationRuntimeIdentity NewRuntime(int seed = 1, string? description = null)
    {
        var runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, description);
        _ = new SimulationScheduler(
            runtime,
            new SimulationSeedAuthority(seed),
            Origin,
            TimeZoneInfo.Utc,
            SimulationCryptoRandomnessPolicy.Reject);
        return runtime;
    }
}
