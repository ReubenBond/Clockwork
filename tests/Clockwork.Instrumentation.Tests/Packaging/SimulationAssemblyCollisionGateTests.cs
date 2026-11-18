using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Packaging;

public sealed class SimulationAssemblyCollisionGateTests
{
    [Theory]
    [InlineData("Clockwork.dll")]
    [InlineData("Orleans.Testing.Simulation.dll")]
    [InlineData("Orleans.Testing.Simulation.Testing.dll")]
    public void ABoundaryContainingOnlyOneAssemblyFamilyIsAccepted(string assembly) =>
        SimulationAssemblyCollisionGate.AssertNoCollision("probe", [assembly]);

    [Fact]
    public void LegacyAndFinalSimulationAssembliesInOneBoundaryAreRejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            SimulationAssemblyCollisionGate.AssertNoCollision(
                "probe",
                [
                    @"packages\clockwork.simulation\lib\net11.0\Clockwork.dll",
                    "Orleans.Testing.Simulation.Testing, Version=1.0.0.0",
                ]));

        Assert.Contains("legacy Clockwork.dll", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Orleans.Testing.Simulation.Testing", exception.Message, StringComparison.Ordinal);
    }
}
