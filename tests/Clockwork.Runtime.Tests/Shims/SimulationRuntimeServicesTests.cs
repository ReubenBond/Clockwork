using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

/// <summary>
/// Covers the token-gated <see cref="SimulationRuntimeServices"/> registry and the
/// <see cref="SimulationRuntimeDispatch"/> three-way contract (inactive, active+registered,
/// active+missing).
/// </summary>
public sealed class SimulationRuntimeServicesTests
{
    [Fact]
    public void RegisterRequiresNonNullArguments()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = ShimTestHarness.NewRuntime();
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        Assert.Throws<ArgumentNullException>(() => SimulationRuntimeServices.Register(null!, runtime, env));
        Assert.Throws<ArgumentNullException>(() => SimulationRuntimeServices.Register(token, null!, env));
        Assert.Throws<ArgumentNullException>(() => SimulationRuntimeServices.Register(token, runtime, null!));
    }

    [Fact]
    public void RegisterThenTryGetResolvesTheEnvironmentAndDisposeUnregisters()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = ShimTestHarness.NewRuntime();
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var registration = SimulationRuntimeServices.Register(token, runtime, env);
        try
        {
            Assert.True(SimulationRuntimeServices.TryGet(runtime, out var resolved));
            Assert.Same(env, resolved);
        }
        finally
        {
            registration.Dispose();
        }

        Assert.False(SimulationRuntimeServices.TryGet(runtime, out var afterDispose));
        Assert.Null(afterDispose);
    }

    [Fact]
    public void DoubleDisposeIsSafe()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = ShimTestHarness.NewRuntime();
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        var registration = SimulationRuntimeServices.Register(token, runtime, env);
        registration.Dispose();
        registration.Dispose();

        Assert.False(SimulationRuntimeServices.TryGet(runtime, out _));
    }

    [Fact]
    public void RegisteringTwiceForTheSameRuntimeThrows()
    {
        var token = SimulationRuntimeActivation.CreateToken();
        var runtime = ShimTestHarness.NewRuntime();
        var env = ShimTestHarness.CreateEnvironment(ShimTestHarness.CreateClock());

        using (SimulationRuntimeServices.Register(token, runtime, env))
        {
            Assert.Throws<InvalidOperationException>(() => SimulationRuntimeServices.Register(token, runtime, env));
        }
    }

    [Fact]
    public void DispatchReturnsFalseOutsideSimulation()
    {
        Assert.False(SimulationExecutionContext.IsActive);
        Assert.False(SimulationRuntimeDispatch.TryGetEnvironment("test.api", out _, out var node));
        Assert.Null(node);
    }

    [Fact]
    public void DispatchReturnsTrueWithEnvironmentAndNodeWhenActiveAndRegistered()
    {
        var clock = ShimTestHarness.CreateClock();
        var env = ShimTestHarness.CreateEnvironment(clock);

        ShimTestHarness.RunInSimulation(env, () =>
        {
            Assert.True(SimulationRuntimeDispatch.TryGetEnvironment("test.api", out var resolved, out var node));
            Assert.Same(env, resolved);
            Assert.NotNull(node);
            Assert.Equal(ShimTestHarness.DefaultNodeAddress, node!.Address);
        });
    }

    [Fact]
    public void DispatchThrowsWhenActiveButNoEnvironmentRegistered()
    {
        ShimTestHarness.RunInSimulationWithoutEnvironment(() =>
        {
            var ex = Assert.Throws<SimulationServiceMissingException>(
                () => SimulationRuntimeDispatch.TryGetEnvironment("System.Example.Api", out _, out _));
            Assert.Equal("System.Example.Api", ex.ApiName);
        });
    }

    [Fact]
    public void ParallelRuntimesResolveTheirOwnEnvironments()
    {
        var clockA = ShimTestHarness.CreateClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var clockB = ShimTestHarness.CreateClock(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var envA = ShimTestHarness.CreateEnvironment(clockA);
        var envB = ShimTestHarness.CreateEnvironment(clockB);

        var runtimeA = ShimTestHarness.NewRuntime(description: "A");
        var runtimeB = ShimTestHarness.NewRuntime(description: "B");

        clockA.Advance(TimeSpan.FromHours(1));
        clockB.Advance(TimeSpan.FromHours(2));

        var utcA = ShimTestHarness.RunInSimulation(envA, DeterministicClock.GetUtcNow, runtime: runtimeA);
        var utcB = ShimTestHarness.RunInSimulation(envB, DeterministicClock.GetUtcNow, runtime: runtimeB);

        Assert.Equal(new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc), utcA);
        Assert.Equal(new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc), utcB);
    }
}
