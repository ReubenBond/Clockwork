namespace Clockwork.Runtime.Tests;

public sealed class RuntimeScaffoldTests
{
    [Fact]
    public void RuntimeAssemblyIsNamedForItsFuturePackage()
    {
        var assembly = System.Reflection.Assembly.Load("Clockwork.Runtime");

        Assert.Equal("Clockwork.Runtime", assembly.GetName().Name);
    }

    [Fact]
    public void RuntimeDoesNotDependOnAnyDownstreamScaffoldProject()
    {
        // Clockwork.Runtime is the foundation project; nothing built on top of it should ever
        // appear as one of its references, or the dependency graph has been inverted.
        var assembly = System.Reflection.Assembly.Load("Clockwork.Runtime");
        var referencedNames = assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        Assert.DoesNotContain("Clockwork.Instrumentation", referencedNames);
        Assert.DoesNotContain("Clockwork.Instrumentation.Build", referencedNames);
        Assert.DoesNotContain("Clockwork.Hosting", referencedNames);
        Assert.DoesNotContain("Clockwork.Http", referencedNames);
        Assert.DoesNotContain("Clockwork.Testing", referencedNames);
        Assert.DoesNotContain("Clockwork.Tool", referencedNames);
    }
}
