using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Clockwork.Runtime.Shims;

namespace Clockwork.Runtime.Tests.Shims;

public sealed partial class ControlledShimOrganizationTests
{
    [Fact]
    public void ControlledTypesMirrorFrameworkNamesAndNamespaces()
    {
        Type[] frameworkTypes =
        [
            .. new[]
            {
                typeof(object).Assembly,
                typeof(System.Diagnostics.Stopwatch).Assembly,
                typeof(System.Threading.Barrier).Assembly,
                typeof(System.Threading.Tasks.Parallel).Assembly,
                typeof(RandomNumberGenerator).Assembly,
                typeof(System.Timers.Timer).Assembly,
            }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes()),
        ];
        Type[] controlledTypes = typeof(SimulationRuntimeDispatch).Assembly
            .GetTypes()
            .Where(type => type.Name.StartsWith("Controlled", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controlledTypes);
        foreach (Type controlledType in controlledTypes)
        {
            string shimNamespace = Assert.IsType<string>(controlledType.Namespace);
            Assert.StartsWith("Clockwork.Shims.", shimNamespace, StringComparison.Ordinal);

            string frameworkNamespace = shimNamespace["Clockwork.Shims.".Length..];
            string frameworkName = TrimGenericArity(controlledType.Name["Controlled".Length..]);
            Assert.True(
                frameworkTypes.Any(
                    type => type.Namespace == frameworkNamespace && TrimGenericArity(type.Name) == frameworkName),
                $"{controlledType.FullName} does not mirror a framework type named {frameworkNamespace}.{frameworkName}.");
        }
    }

    [Fact]
    public void ControlledTypeDeclarationsAreUnderShimDirectory()
    {
        string repositoryRoot = FindRepositoryRoot();
        string runtimeRoot = Path.Combine(repositoryRoot, "src", "Clockwork", "Runtime");
        string shimRoot = Path.Combine(runtimeRoot, "Shims") + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string sourceFile in Directory.EnumerateFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (!ControlledTypeDeclaration().IsMatch(File.ReadAllText(sourceFile)))
            {
                continue;
            }

            Assert.True(
                Path.GetFullPath(sourceFile).StartsWith(shimRoot, pathComparison),
                $"Controlled type declaration must be beneath '{shimRoot}': {sourceFile}");
        }
    }

    private static string TrimGenericArity(string typeName)
    {
        int arityMarker = typeName.IndexOf('`', StringComparison.Ordinal);
        return arityMarker < 0 ? typeName : typeName[..arityMarker];
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(sourceFile)!);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clockwork.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Clockwork repository root.");
    }

    [GeneratedRegex(
        @"^\s*(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?|interface|enum)\s+Controlled\w+",
        RegexOptions.Multiline)]
    private static partial Regex ControlledTypeDeclaration();
}
