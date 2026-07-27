namespace Clockwork.Runtime.Tests;

public sealed class RuntimeBoundaryTests
{
    [Fact]
    public void RuntimeAssemblyHasExpectedName()
    {
        var assembly = System.Reflection.Assembly.Load("Clockwork.Runtime");

        Assert.Equal("Clockwork.Runtime", assembly.GetName().Name);
    }

    [Fact]
    public void RuntimeDoesNotDependOnAnyDownstreamProject()
    {
        var assembly = System.Reflection.Assembly.Load("Clockwork.Runtime");
        var referencedNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Clockwork.Instrumentation", referencedNames);
        Assert.DoesNotContain("Clockwork.Instrumentation.Build", referencedNames);
        Assert.DoesNotContain("Clockwork.Testing", referencedNames);
        Assert.DoesNotContain("Clockwork.Tool", referencedNames);
    }
}
