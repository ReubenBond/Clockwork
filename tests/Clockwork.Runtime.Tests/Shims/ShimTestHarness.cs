using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Test helpers for driving the deterministic shims: entering an active simulation scope (optionally
/// with a node), registering an <see cref="ISimulationRuntimeEnvironment"/>, and building a default
/// <see cref="SimulationRuntimeEnvironment"/> with a controllable virtual clock.
/// </summary>
internal static class ShimTestHarness
{
    public const string DefaultNodeAddress = "10.0.0.1";

    /// <summary>The fixed virtual origin used by the default test environment.</summary>
    public static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static SimulationRuntimeIdentity NewRuntime(int seed = 12345, string? description = null) =>
        new(Guid.NewGuid(), seed, description);

    public static MutableClock CreateClock(DateTimeOffset? start = null) => new(start ?? Origin);

    public static SimulationRuntimeEnvironment CreateEnvironment(
        MutableClock clock,
        int rootSeed = 12345,
        TimeZoneInfo? localTimeZone = null,
        SimulationCryptoRandomnessPolicy cryptoPolicy = SimulationCryptoRandomnessPolicy.Reject) =>
        new(
            new SimulationSeedAuthority(rootSeed),
            () => clock.UtcNow,
            localTimeZone ?? TimeZoneInfo.Utc,
            Origin,
            cryptoPolicy);

    /// <summary>
    /// Runs <paramref name="body"/> inside an active simulation with the given environment registered
    /// and the default node entered. Tears everything down afterwards.
    /// </summary>
    public static T RunInSimulation<T>(
        ISimulationRuntimeEnvironment environment,
        Func<T> body,
        string? nodeAddress = DefaultNodeAddress,
        SimulationRuntimeIdentity? runtime = null)
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var activeRuntime = runtime ?? NewRuntime();

        using (SimulationRuntimeServices.Register(token, activeRuntime, environment))
        using (SimulationExecutionContext.EnterRuntime(token, activeRuntime))
        {
            if (nodeAddress is null)
            {
                return body();
            }

            using (SimulationExecutionContext.EnterNode(new SimulationNodeIdentity(nodeAddress)))
            {
                return body();
            }
        }
    }

    public static void RunInSimulation(
        ISimulationRuntimeEnvironment environment,
        Action body,
        string? nodeAddress = DefaultNodeAddress,
        SimulationRuntimeIdentity? runtime = null)
    {
        RunInSimulation<object?>(
            environment,
            () =>
            {
                body();
                return null;
            },
            nodeAddress,
            runtime);
    }

    /// <summary>
    /// Runs <paramref name="body"/> inside an active simulation with a node entered but <em>no</em>
    /// environment registered, to exercise the missing-service failure path.
    /// </summary>
    public static void RunInSimulationWithoutEnvironment(Action body, string? nodeAddress = DefaultNodeAddress)
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = NewRuntime();

        using (SimulationExecutionContext.EnterRuntime(token, runtime))
        {
            if (nodeAddress is null)
            {
                body();
                return;
            }

            using (SimulationExecutionContext.EnterNode(new SimulationNodeIdentity(nodeAddress)))
            {
                body();
            }
        }
    }

    /// <summary>A simple mutable virtual clock for tests.</summary>
    internal sealed class MutableClock(DateTimeOffset start)
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }
}
