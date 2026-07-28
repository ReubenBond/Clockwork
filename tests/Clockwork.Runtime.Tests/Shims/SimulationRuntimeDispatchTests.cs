using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

public sealed class SimulationRuntimeDispatchTests
{
    [Fact]
    public void RequireEnvironmentOutsideSimulationRequiresActiveSimulation()
    {
        Assert.False(SimulationExecutionContext.IsActive);

        Exception? exception = Record.Exception(
            () => SimulationRuntimeDispatch.RequireEnvironment("test.api"));

        SimulationNotActiveExceptionAssert.Equal(exception, "test.api");
    }

    [Fact]
    public void RequireEnvironmentReturnsAmbientEnvironmentAndNode()
    {
        var clock = ShimTestHarness.CreateClock();
        var environment = ShimTestHarness.CreateEnvironment(clock);

        ShimTestHarness.RunInSimulation(environment, () =>
        {
            var (_, resolved, node) = SimulationRuntimeDispatch.RequireEnvironment("test.api");
            Assert.Equal(environment.CryptoPolicy, resolved.CryptoPolicy);
            Assert.Equal(environment.GetUtcNow(node), resolved.GetUtcNow(node));
            Assert.Equal(ShimTestHarness.DefaultNodeAddress, node?.Address);
        });
    }

    [Fact]
    public void ParallelRuntimesCarryIndependentEnvironments()
    {
        var clockA = ShimTestHarness.CreateClock();
        var clockB = ShimTestHarness.CreateClock();
        var environmentA = ShimTestHarness.CreateEnvironment(clockA);
        var environmentB = ShimTestHarness.CreateEnvironment(clockB);
        var runtimeA = ShimTestHarness.NewRuntime(description: "A");
        var runtimeB = ShimTestHarness.NewRuntime(description: "B");
        clockA.Advance(TimeSpan.FromHours(1));
        clockB.Advance(TimeSpan.FromHours(2));

        var utcA = ShimTestHarness.RunInSimulation(environmentA, ControlledDateTime.GetUtcNow, runtime: runtimeA);
        var utcB = ShimTestHarness.RunInSimulation(environmentB, ControlledDateTime.GetUtcNow, runtime: runtimeB);

        Assert.Equal(new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc), utcA);
        Assert.Equal(new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc), utcB);
    }
}
