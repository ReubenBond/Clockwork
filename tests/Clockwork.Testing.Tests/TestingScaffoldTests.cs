namespace Clockwork.Testing.Tests;

public sealed class TestingScaffoldTests
{
    [Fact]
    public void TestingAssemblyIsNamedForItsFuturePackage()
    {
        var assembly = System.Reflection.Assembly.Load("Clockwork.Testing");

        Assert.Equal("Clockwork.Testing", assembly.GetName().Name);
    }

    [Fact]
    public void TestingDependsOnClockworkButNotOnInstrumentationOrTool()
    {
        // Testing helpers should only need Clockwork, not the instrumentation or
        // tooling layers built on top of it.
        var assembly = System.Reflection.Assembly.Load("Clockwork.Testing");
        var referencedNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.Contains("Clockwork", referencedNames);
        Assert.DoesNotContain("Clockwork.Instrumentation", referencedNames);
        Assert.DoesNotContain("Clockwork.Tool", referencedNames);
    }
}
