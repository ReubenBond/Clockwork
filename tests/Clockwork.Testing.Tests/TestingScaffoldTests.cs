namespace Orleans.Testing.Simulation.Testing.Tests;

public sealed class TestingScaffoldTests
{
    [Fact]
    public void TestingAssemblyIsNamedForItsFuturePackage()
    {
        var assembly = System.Reflection.Assembly.Load("Orleans.Testing.Simulation.Testing");

        Assert.Equal("Orleans.Testing.Simulation.Testing", assembly.GetName().Name);
    }

    [Fact]
    public void TestingDependsOnClockworkButNotOnInstrumentationOrTool()
    {
        // Testing helpers should only need Clockwork, not the instrumentation or
        // tooling layers built on top of it.
        var assembly = System.Reflection.Assembly.Load("Orleans.Testing.Simulation.Testing");
        var referencedNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.Contains("Orleans.Testing.Simulation", referencedNames);
        Assert.DoesNotContain("Orleans.Testing.Simulation.Instrumentation", referencedNames);
        Assert.DoesNotContain("Orleans.Testing.Simulation.Tool", referencedNames);
    }
}
