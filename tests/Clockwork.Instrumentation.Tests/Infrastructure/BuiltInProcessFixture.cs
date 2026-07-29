using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Runtime.Tasks;
using Microsoft.CodeAnalysis;

namespace Clockwork.Instrumentation.Tests.Infrastructure;

/// <summary>
/// Builds a real executable plus a third-party dependency which uses only ordinary BCL APIs, rewrites
/// both with Clockwork's built-in rules, and runs the source/staged closures in independent processes.
/// </summary>
internal sealed class BuiltInProcessFixture : IDisposable
{
    private const string AppAssemblyName = "Fx.BuiltInApp";
    private const string DependencyAssemblyName = "ThirdParty.Sync";

    private const string DependencySource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace ThirdParty.Sync
        {
            public static class Probe
            {
                public static string Run()
                {
                    string monitor;
                    try { _ = Monitor.LockContentionCount; monitor = "real"; }
                    catch (Exception ex) when (ex.GetType().Name == "SimulationApiException") { monitor = "rejected"; }

                    var dedicated = new Lock();
                    lock (dedicated) { }

                    string semaphore;
                    using (var slim = new SemaphoreSlim(1, 1))
                    {
                        slim.Wait();
                        slim.Release();
                        // AvailableWaitHandle is controlled: a permit is available
                        // (count == 1) so the bridged handle is signalled in both source and staged runs.
                        WaitHandle h = slim.AvailableWaitHandle;
                        semaphore = h.WaitOne(0) ? "signaled" : "unsignaled";
                    }

                    Func<Task>[] delays =
                    [
                        () => Task.Delay(0),
                        () => Task.Delay(TimeSpan.Zero),
                        () => Task.Delay(0, CancellationToken.None),
                        () => Task.Delay(TimeSpan.Zero, CancellationToken.None),
                        () => Task.Delay(TimeSpan.Zero, TimeProvider.System),
                        () => Task.Delay(TimeSpan.Zero, TimeProvider.System, CancellationToken.None),
                    ];
                    int completedDelays = 0;
                    foreach (Func<Task> delay in delays)
                    {
                        if (delay().IsCompletedSuccessfully) { completedDelays++; }
                    }

                    using var timer = new Timer(_ => { });
                    return $"monitor={monitor};lock={dedicated.GetType().FullName};semaphore={semaphore};delays={completedDelays};timer={timer.GetType().FullName}";
                }
            }
        }
        """;

    private const string AppSource = """
        using System;
        using Clockwork;
        using ThirdParty.Sync;

        public static class Program
        {
            public static int Main()
            {
                var simulation = new SimulationCluster(seed: 1);
                var node = simulation.AddNode("node");
                string output = "";
                node.Context.SchedulerLane.EnqueueAfter(() => output = Probe.Run(), TimeSpan.Zero);
                simulation.RunUntilIdle(System.Threading.CancellationToken.None);
                Console.WriteLine(output);
                return 0;
            }
        }
        """;

    private BuiltInProcessFixture(string root, string sourceDirectory, string stagingDirectory)
    {
        Root = root;
        SourceDirectory = sourceDirectory;
        StagingDirectory = stagingDirectory;
    }

    public string Root { get; }

    public string SourceDirectory { get; }

    public string StagingDirectory { get; }

    public string SourceAppPath => Path.Combine(SourceDirectory, AppAssemblyName + ".dll");

    public string StagedAppPath => Path.Combine(StagingDirectory, AppAssemblyName + ".dll");

    public static BuiltInProcessFixture Create(bool optimize)
    {
        string root = TestArtifacts.CreateUnique("cwr-builtins-process");
        string source = Path.Combine(root, "source");
        string staging = Path.Combine(root, "staged");
        Directory.CreateDirectory(source);

        var fixture = new BuiltInProcessFixture(root, source, staging);
        string clockworkBuild = ClockworkAssemblyPath();
        string clockwork = CopyAssembly(clockworkBuild, source);
        string runtime = CopyAssembly(typeof(ControlledTask).Assembly.Location, source);
        CopyPackageAssembly("microsoft.extensions.logging.abstractions", source);
        CopyPackageAssembly("microsoft.extensions.dependencyinjection.abstractions", source);

        string dependency = FixtureCompiler.Compile(
            DependencyAssemblyName,
            DependencySource,
            source,
            FixtureSymbols.PortableFile,
            optimize);
        FixtureCompiler.Compile(
            AppAssemblyName,
            AppSource,
            source,
            FixtureSymbols.PortableFile,
            optimize,
            [dependency, clockwork, runtime],
            outputKind: OutputKind.ConsoleApplication);
        ProcessAppRunner.WriteRuntimeConfig(fixture.SourceAppPath);
        return fixture;
    }

    public InstrumentationResult Instrument() =>
        InstrumentationRunner.Run(new InstrumentationRequest
        {
            SourceDirectory = SourceDirectory,
            StagingDirectory = StagingDirectory,
            EntryAssemblyName = AppAssemblyName,
            Configuration = new InstrumentationConfiguration
            {
                IncludePatterns = [AppAssemblyName + ".dll", DependencyAssemblyName + ".dll"],
                TargetRuntime = new Version(10, 0),
            },
            RuleSet = BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies),
        });

    public AppRunResult RunSource() => ProcessAppRunner.Run(SourceAppPath);

    public AppRunResult RunStaged() => ProcessAppRunner.Run(StagedAppPath);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CopyAssembly(string path, string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, Path.GetFileName(path));
        File.Copy(path, destination, overwrite: true);
        return destination;
    }

    private static void CopyPackageAssembly(string packageId, string destinationDirectory)
    {
        string packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string packageDirectory = Path.Combine(packages, packageId);
        string assembly = Directory.GetFiles(packageDirectory, "*.dll", SearchOption.AllDirectories)
            .First(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        CopyAssembly(assembly, destinationDirectory);
    }

    private static string ClockworkAssemblyPath()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = baseDirectory.Parent?.Name ?? "Release";
        DirectoryInfo? current = baseDirectory;
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Clockwork.slnx")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Could not locate Clockwork.slnx.");
        }

        return Path.Combine(current.FullName, "src", "Clockwork", "bin", configuration, "net10.0", "Clockwork.dll");
    }
}
