namespace Clockwork.Instrumentation.Tests;

public sealed class InstrumentationScaffoldTests
{
    [Fact]
    public void InstrumentationAssemblyIsNamedForItsFuturePackage()
    {
        var assembly = System.Reflection.Assembly.Load("Clockwork.Instrumentation");

        Assert.Equal("Clockwork.Instrumentation", assembly.GetName().Name);
    }

    [Fact]
    public void InstrumentationDependsOnRuntimeButNotOnBuildOrTool()
    {
        // Instrumentation sits above Runtime and below Instrumentation.Build/Tool - verify the
        // dependency edge points the right way.
        var assembly = System.Reflection.Assembly.Load("Clockwork.Instrumentation");
        var referencedNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.Contains("Clockwork.Runtime", referencedNames);
        Assert.DoesNotContain("Clockwork.Instrumentation.Build", referencedNames);
        Assert.DoesNotContain("Clockwork.Tool", referencedNames);
    }
}
