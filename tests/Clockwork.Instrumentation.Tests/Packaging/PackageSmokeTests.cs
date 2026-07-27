using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Xml.Linq;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Tests.Infrastructure;

namespace Clockwork.Instrumentation.Tests.Packaging;

/// <summary>
/// End-to-end package smoke tests. These pack the real <c>Clockwork.Instrumentation.Build</c> and
/// <c>Clockwork.Tool</c> NuGet packages into an isolated local feed, then consume them exactly as an
/// end user would - the build package via a <c>PackageReference</c> that runs the real MSBuild
/// targets during <c>dotnet build</c>, and the tool via <c>dotnet tool install</c> followed by
/// invoking the installed <c>clockwork</c> command. Project references are deliberately avoided so
/// the packaging metadata itself (task/props/targets layout, tool bundling) is exercised.
/// </summary>
/// <remarks>
/// They are gated behind the <c>CLOCKWORK_SMOKE_TESTS=1</c> environment variable because each test
/// runs several full SDK operations (pack, restore, build, tool install) and is far slower than a
/// unit test. CI enables them in a dedicated step; a normal local test run skips them.
/// </remarks>
public sealed class PackageSmokeTests
{
    private static readonly bool SmokeEnabled =
        string.Equals(Environment.GetEnvironmentVariable("CLOCKWORK_SMOKE_TESTS"), "1", StringComparison.Ordinal);

    private static readonly Lazy<PackagedArtifacts> Artifacts = new(PackagedArtifacts.Build);

    [Fact]
    public void BuildPackageInstrumentsAppWhenEnabled()
    {
        Assert.SkipUnless(SmokeEnabled, "Set CLOCKWORK_SMOKE_TESTS=1 to run package smoke tests.");
        PackagedArtifacts artifacts = Artifacts.Value;

        ConsumerProject consumer = artifacts.ScaffoldConsumer("EnabledApp", instrumentationEnabled: true);
        AppRunResult build = consumer.Build();
        Assert.True(build.ExitCode == 0, $"Build failed:\n{build.StandardOutput}\n{build.StandardError}");

        // The opt-in build emits the manifest to the predictable intermediate path.
        Assert.True(File.Exists(consumer.ManifestPath), $"Manifest not found at {consumer.ManifestPath}");

        // The uninstrumented output behaves normally; the staged, instrumented output dispatches to
        // the shim - proving the packaged targets rewrote the real build output out-of-place.
        AppRunResult normal = ProcessAppRunner.Run(consumer.OutputAppPath);
        Assert.Equal(0, normal.ExitCode);
        Assert.Contains("ticks=100", normal.Output);

        AppRunResult staged = ProcessAppRunner.Run(consumer.StagedAppPath);
        Assert.Equal(0, staged.ExitCode);
        Assert.Contains("ticks=999", staged.Output);
    }

    [Fact]
    public void BuildPackageDoesNothingWhenDisabled()
    {
        Assert.SkipUnless(SmokeEnabled, "Set CLOCKWORK_SMOKE_TESTS=1 to run package smoke tests.");
        PackagedArtifacts artifacts = Artifacts.Value;

        ConsumerProject consumer = artifacts.ScaffoldConsumer("DisabledApp", instrumentationEnabled: false);
        AppRunResult build = consumer.Build();
        Assert.True(build.ExitCode == 0, $"Build failed:\n{build.StandardOutput}\n{build.StandardError}");

        // An ordinary build must not instrument: no manifest, no staged closure.
        Assert.False(File.Exists(consumer.ManifestPath), "Manifest was emitted on a non-opted-in build.");
        Assert.False(Directory.Exists(consumer.StagingDirectory), "Staging directory was created on a non-opted-in build.");

        AppRunResult normal = ProcessAppRunner.Run(consumer.OutputAppPath);
        Assert.Equal(0, normal.ExitCode);
        Assert.Contains("ticks=100", normal.Output);
    }

    [Fact]
    public void InstalledToolReportsVersion()
    {
        Assert.SkipUnless(SmokeEnabled, "Set CLOCKWORK_SMOKE_TESTS=1 to run package smoke tests.");
        PackagedArtifacts artifacts = Artifacts.Value;

        AppRunResult version = artifacts.RunTool(["--version"]);
        Assert.Equal(0, version.ExitCode);
        // The tool reports the engine assembly version, which the smoke pack pinned to 9.9.9.
        Assert.StartsWith("9.9.9", version.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledToolInspectsAssemblyAsJson()
    {
        Assert.SkipUnless(SmokeEnabled, "Set CLOCKWORK_SMOKE_TESTS=1 to run package smoke tests.");
        PackagedArtifacts artifacts = Artifacts.Value;

        string probeDir = Path.Combine(artifacts.Root, "probe");
        Directory.CreateDirectory(probeDir);
        string assembly = FixtureCompiler.Compile(
            "SmokeProbe",
            "namespace SmokeProbe { public static class P { public static int V() => 1; } }",
            probeDir,
            FixtureSymbols.PortableFile,
            optimize: false);

        AppRunResult inspect = artifacts.RunTool(["inspect", assembly, "--json"]);
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("\"managed\": true", inspect.Output);
        Assert.Contains("SmokeProbe.dll", inspect.Output);
    }

    [Fact]
    public void PackedPackagesCarryLicenseMetadataAndRedistributionNotices()
    {
        Assert.SkipUnless(SmokeEnabled, "Set CLOCKWORK_SMOKE_TESTS=1 to run package smoke tests.");
        PackagedArtifacts artifacts = Artifacts.Value;

        AssertPackageMetadata(artifacts.PackagePath("Clockwork.Instrumentation.Build"));
        AssertPackageMetadata(artifacts.PackagePath("Clockwork.Tool"));
    }

    private static void AssertPackageMetadata(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Contains("LICENSE", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains("THIRD-PARTY-NOTICES.md", entries);
        Assert.Contains(entries, entry => entry.EndsWith("/Mono.Cecil.dll", StringComparison.Ordinal));

        ZipArchiveEntry nuspecEntry = Assert.Single(package.Entries, entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        XElement metadata = nuspec.Root.Element(ns + "metadata")!;
        XElement license = metadata.Element(ns + "license")!;
        Assert.Equal("expression", license.Attribute("type")?.Value);
        Assert.Equal("MIT", license.Value);
        Assert.Equal(
            "https://github.com/ReubenBond/Clockwork",
            metadata.Element(ns + "repository")?.Attribute("url")?.Value);

        ZipArchiveEntry noticesEntry = package.GetEntry("THIRD-PARTY-NOTICES.md")!;
        using var reader = new StreamReader(noticesEntry.Open());
        string notices = reader.ReadToEnd();
        Assert.Contains("Copyright (c) 2008 - 2015 Jb Evain", notices, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2008 - 2011 Novell, Inc.", notices, StringComparison.Ordinal);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", notices, StringComparison.Ordinal);
    }

    /// <summary>
    /// The packed local feed plus the installed tool, built once and shared across the smoke tests.
    /// </summary>
    private sealed class PackagedArtifacts
    {
        private PackagedArtifacts(string root, string feed, string packagesDirectory, string version, string toolPath)
        {
            Root = root;
            _feed = feed;
            _packagesDirectory = packagesDirectory;
            _version = version;
            _toolPath = toolPath;
        }

        public string Root { get; }

        private readonly string _feed;
        private readonly string _packagesDirectory;
        private readonly string _version;
        private readonly string _toolPath;

        public static PackagedArtifacts Build()
        {
            string repoRoot = FindRepositoryRoot();
            string root = TestArtifacts.CreateUnique("cwr-smoke");
            string feed = Path.Combine(root, "feed");
            string packages = Path.Combine(root, "packages");
            string toolPath = Path.Combine(root, "tool");
            Directory.CreateDirectory(feed);
            Directory.CreateDirectory(packages);

            // A unique prerelease version per run guarantees the consumer resolves these freshly
            // packed artifacts rather than any identically named package in a shared cache.
            string version = "9.9.9-smoke." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);

            Pack(repoRoot, "src/Clockwork.Instrumentation.Build/Clockwork.Instrumentation.Build.csproj", version, feed, packages);
            Pack(repoRoot, "src/Clockwork.Tool/Clockwork.Tool.csproj", version, feed, packages);

            InstallTool(feed, packages, toolPath, version);

            return new PackagedArtifacts(root, feed, packages, version, toolPath);
        }

        public AppRunResult RunTool(IReadOnlyList<string> arguments)
        {
            string executable = Path.Combine(
                _toolPath, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "clockwork.exe" : "clockwork");
            return ProcessAppRunner.Execute(executable, arguments, _toolPath, timeout: TimeSpan.FromSeconds(120));
        }

        public string PackagePath(string packageId) =>
            Directory.GetFiles(_feed, "*.nupkg")
                .Single(path => Path.GetFileName(path).StartsWith(
                    packageId + "." + _version,
                    StringComparison.OrdinalIgnoreCase));

        public ConsumerProject ScaffoldConsumer(string name, bool instrumentationEnabled)
        {
            string rootDir = Path.Combine(Root, name);
            string appDir = Path.Combine(rootDir, "app");
            string libDir = Path.Combine(rootDir, "lib");
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(libDir);

            // A single nuget.config at the solution root is discovered by both projects via the
            // standard walk-up, wiring in the freshly packed local feed.
            File.WriteAllText(Path.Combine(rootDir, "nuget.config"), $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="clockwork-local" value="{_feed}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            // The controlled API and the shim live in one dependency assembly, in its own directory
            // so the executable project's default source glob does not also compile it. The redirect
            // rule points at the shim, so the runner copies this assembly verbatim while rewriting
            // the app.
            File.WriteAllText(Path.Combine(libDir, "SmokeApi.cs"), """
                namespace SmokeApi
                {
                    public static class RealClock
                    {
                        public static long UtcNowTicks() => 100L;
                    }

                    public static class Shim
                    {
                        public static long UtcNowTicks() => 999L;
                    }
                }
                """);
            File.WriteAllText(Path.Combine(libDir, "SmokeApi.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <AssemblyName>SmokeApi</AssemblyName>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(appDir, "Program.cs"), """
                using SmokeApi;

                System.Console.WriteLine("ticks=" + RealClock.UtcNowTicks());
                """);

            string enabled = instrumentationEnabled ? "true" : "false";
            File.WriteAllText(Path.Combine(appDir, "SmokeApp.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <AssemblyName>SmokeApp</AssemblyName>
                    <ClockworkInstrumentationEnabled>{enabled}</ClockworkInstrumentationEnabled>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Clockwork.Instrumentation.Build" Version="{_version}" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="../lib/SmokeApi.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <ClockworkRuleSet Include="$(MSBuildProjectDirectory)/clockwork.rules.json" />
                  </ItemGroup>
                </Project>
                """);

            var ruleSet = new RewriteRuleSet(
                "smoke.rules",
                "1.0",
                [
                    RewriteRule.RedirectCall(
                        "redirect-utcnowticks",
                        MemberSignature.Method("SmokeApi.RealClock", "UtcNowTicks"),
                        RewriteReplacement.Method("SmokeApi", "SmokeApi.Shim", "UtcNowTicks")),
                ]);
            File.WriteAllText(Path.Combine(appDir, "clockwork.rules.json"), RuleSetJson.Write(ruleSet));

            return new ConsumerProject(appDir, _packagesDirectory);
        }

        private static void Pack(string repoRoot, string relativeProject, string version, string feed, string packages)
        {
            AppRunResult result = ProcessAppRunner.Execute(
                "dotnet",
                ["pack", relativeProject, "-c", "Release", $"-p:Version={version}", "-o", feed, "--nologo"],
                repoRoot,
                NuGetEnvironment(packages),
                TimeSpan.FromSeconds(300));

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Packing '{relativeProject}' failed:\n{result.StandardOutput}\n{result.StandardError}");
            }
        }

        private static void InstallTool(string feed, string packages, string toolPath, string version)
        {
            AppRunResult result = ProcessAppRunner.Execute(
                "dotnet",
                ["tool", "install", "Clockwork.Tool", "--version", version, "--tool-path", toolPath, "--add-source", feed],
                feed,
                NuGetEnvironment(packages),
                TimeSpan.FromSeconds(300));

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Installing the clockwork tool failed:\n{result.StandardOutput}\n{result.StandardError}");
            }
        }

        private static Dictionary<string, string> NuGetEnvironment(string packagesDirectory) => new()
        {
            ["NUGET_PACKAGES"] = packagesDirectory,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
        };

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Clockwork.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not locate the repository root (Clockwork.slnx).");
        }
    }

    /// <summary>A scaffolded consumer project that references the packed build package.</summary>
    private sealed class ConsumerProject
    {
        private const string OutputRelative = "bin/Release/net10.0";
        private const string StagingRelative = "obj/Release/net10.0/clockwork/instrumented";
        private const string ManifestRelative = "obj/Release/net10.0/clockwork/clockwork.manifest.json";

        private readonly string _packagesDirectory;

        public ConsumerProject(string projectDirectory, string packagesDirectory)
        {
            ProjectDirectory = projectDirectory;
            _packagesDirectory = packagesDirectory;
        }

        public string ProjectDirectory { get; }

        public string OutputAppPath => Path.Combine(ProjectDirectory, OutputRelative, "SmokeApp.dll");

        public string StagingDirectory => Path.Combine(ProjectDirectory, StagingRelative);

        public string StagedAppPath => Path.Combine(StagingDirectory, "SmokeApp.dll");

        public string ManifestPath => Path.Combine(ProjectDirectory, ManifestRelative);

        public AppRunResult Build() => ProcessAppRunner.Execute(
            "dotnet",
            ["build", "SmokeApp.csproj", "-c", "Release", "--nologo"],
            ProjectDirectory,
            new Dictionary<string, string>
            {
                ["NUGET_PACKAGES"] = _packagesDirectory,
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                ["DOTNET_NOLOGO"] = "1",
            },
            TimeSpan.FromSeconds(300));
    }
}
