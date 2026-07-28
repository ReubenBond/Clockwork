using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Imaging;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Signing;

namespace Clockwork.Instrumentation.Orchestration;

/// <summary>
/// The deterministic orchestrator that turns an application output/publish directory into an
/// instrumented closure staged in a separate directory. It discovers the closure, enforces the
/// configured ReadyToRun and strong-name policies, rewrites managed IL with the Phase&#160;4A
/// <see cref="RewriteEngine"/>, copies every non-rewritten asset verbatim, emits a deterministic
/// closure manifest, and maintains an incremental cache keyed by every input's content hash plus the
/// engine, rule-set, and configuration signatures. The source directory is never modified.
/// </summary>
public static class InstrumentationRunner
{
    /// <summary>Runs the instrumentation over the closure described by <paramref name="request"/>.</summary>
    /// <param name="request">The instrumentation request.</param>
    /// <returns>The deterministic outcome, including per-assembly results and diagnostics.</returns>
    /// <exception cref="ClosureException">The source directory is missing or the entry cannot be determined.</exception>
    public static InstrumentationResult Run(InstrumentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string sourceDirectory = Path.GetFullPath(request.SourceDirectory);
        string stagingDirectory = Path.GetFullPath(request.StagingDirectory);
        InstrumentationConfiguration configuration = request.Configuration;
        ValidateStagingDirectory(sourceDirectory, stagingDirectory);

        var topLevel = new List<RewriteDiagnostic>();

        // Load the strong-name key up front so a bad key path fails fast and clearly.
        StrongNameKey? key = null;
        if (configuration.StrongNamePolicy == StrongNamePolicy.ReSign)
        {
            if (string.IsNullOrEmpty(configuration.StrongNameKeyPath))
            {
                topLevel.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.StrongNameReSignRequired,
                    "Strong-name policy is 'ReSign' but no strong-name key path is configured."));
            }
            else
            {
                try
                {
                    key = StrongNameKey.Load(configuration.StrongNameKeyPath);
                    if (!key.CanSign)
                    {
                        topLevel.Add(RewriteDiagnostic.Error(
                            RewriteDiagnosticIds.StrongNameReSignRequired,
                            $"Strong-name key '{configuration.StrongNameKeyPath}' is a public-only key and cannot sign."));
                        key = null;
                    }
                }
                catch (Exception ex) when (ex is SigningException or IOException or UnauthorizedAccessException)
                {
                    topLevel.Add(RewriteDiagnostic.Error(
                        RewriteDiagnosticIds.StrongNameReSignRequired,
                        $"Failed to load strong-name key '{configuration.StrongNameKeyPath}': {ex.Message}"));
                }
            }
        }

        ClosurePlan plan = ClosureDiscovery.Discover(sourceDirectory, configuration, request.EntryAssemblyName);

        string incrementalKey = ComputeIncrementalKey(plan, configuration, request.RuleSet, key);

        // Incremental short-circuit: identical inputs and outputs already exist.
        if (topLevel.Count == 0
            && File.Exists(request.CachePath)
            && string.Equals(File.ReadAllText(request.CachePath).Trim(), incrementalKey, StringComparison.Ordinal)
            && Directory.Exists(stagingDirectory)
            && File.Exists(request.ManifestPath))
        {
            return new InstrumentationResult
            {
                Succeeded = true,
                WasIncrementalHit = true,
                StagingDirectory = stagingDirectory,
                ManifestPath = request.ManifestPath,
                Diagnostics = [.. topLevel],
            };
        }

        DeleteIfExists(request.CachePath);

        if (topLevel.Count > 0)
        {
            return new InstrumentationResult
            {
                Succeeded = false,
                WasIncrementalHit = false,
                StagingDirectory = stagingDirectory,
                ManifestPath = request.ManifestPath,
                Diagnostics = [.. topLevel],
            };
        }

        PrepareStagingDirectory(stagingDirectory);

        var copied = new List<string>();
        foreach (ClosureAsset asset in plan.AssetsToCopy)
        {
            string destination = ToStagingPath(stagingDirectory, asset.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(asset.SourcePath, destination, overwrite: true);
            copied.Add(asset.RelativePath);
        }

        ImmutableArray<string> replacementPaths =
            ResolveReplacementAssemblies(sourceDirectory, request.RuleSet, configuration);
        HashSet<string> replacementNames = ResolveReplacementNames(request.RuleSet, configuration);
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = replacementPaths,
            ReferenceSearchDirectories = [sourceDirectory],
            TargetRuntime = configuration.TargetRuntime,
            InstrumentRaceExploration = configuration.Mode == InstrumentationMode.RaceExploration,
        };

        var assemblyResults = new List<AssemblyInstrumentationResult>();
        foreach (ClosureAsset asset in plan.AssembliesToRewrite)
        {
            // A rule set's replacement ("shim") assemblies are inputs to the rewrite, not targets of
            // it: rewriting a shim would corrupt it (e.g. redirect its own calls into itself). Copy
            // them verbatim so the staged closure can resolve the redirects at runtime.
            if (replacementNames.Contains(Path.GetFileNameWithoutExtension(asset.RelativePath)))
            {
                string replacementDestination = ToStagingPath(stagingDirectory, asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(replacementDestination)!);
                File.Copy(asset.SourcePath, replacementDestination, overwrite: true);
                copied.Add(asset.RelativePath);
                continue;
            }

            assemblyResults.Add(ProcessAssembly(asset, stagingDirectory, configuration, request.RuleSet, options, key));
        }

        if (configuration.Mode == InstrumentationMode.RaceExploration)
        {
            const string runtimeAsset = "Clockwork.Runtime.dll";
            File.Copy(
                typeof(Runtime.Racing.RaceInstrumentation).Assembly.Location,
                Path.Combine(stagingDirectory, runtimeAsset),
                overwrite: true);
            if (!copied.Contains(runtimeAsset, StringComparer.Ordinal))
            {
                copied.Add(runtimeAsset);
            }
        }

        bool succeeded = topLevel.All(d => !d.IsError) && !assemblyResults.SelectMany(a => a.Errors).Any();

        ClosureManifest manifest = BuildManifest(plan, configuration, request.RuleSet, incrementalKey, assemblyResults, copied);
        WriteAllTextAtomically(request.ManifestPath, manifest.ToJson());

        if (succeeded)
        {
            WriteAllTextAtomically(request.CachePath, incrementalKey);
        }

        return new InstrumentationResult
        {
            Succeeded = succeeded,
            WasIncrementalHit = false,
            StagingDirectory = stagingDirectory,
            ManifestPath = request.ManifestPath,
            Assemblies = [.. assemblyResults],
            CopiedAssets = [.. copied],
            Diagnostics = [.. topLevel],
        };
    }

    private static AssemblyInstrumentationResult ProcessAssembly(
        ClosureAsset asset,
        string stagingDirectory,
        InstrumentationConfiguration configuration,
        Rules.RewriteRuleSet ruleSet,
        RewriteOptions options,
        StrongNameKey? key)
    {
        string inputPath = asset.SourcePath;
        string outputPath = ToStagingPath(stagingDirectory, asset.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var diagnostics = new List<RewriteDiagnostic>();

        AssemblyImageInfo image;
        try
        {
            image = AssemblyImageInfo.Inspect(inputPath);
        }
        catch (BadImageFormatException ex)
        {
            diagnostics.Add(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.ValidationFailed,
                $"'{asset.RelativePath}' is not a valid PE image: {ex.Message}"));
            return new AssemblyInstrumentationResult(asset.RelativePath, false, false, false, false, null, [.. diagnostics]);
        }

        // ReadyToRun images clear the ILOnly CLI flag, so they must be classified before the
        // mixed-mode check (which keys off ILOnly) or a genuine R2R image would be misreported.
        bool readyToRunStripped = false;
        string engineInput = inputPath;
        if (image.IsReadyToRun)
        {
            if (configuration.ReadyToRunPolicy == ReadyToRunPolicy.Reject)
            {
                diagnostics.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.ReadyToRunRejected,
                    $"'{asset.RelativePath}' is a ReadyToRun image. Set readyToRunPolicy to 'StripToIL' to rewrite it as IL-only, or publish without ReadyToRun. Instrumentation must run before ReadyToRun/AOT/single-file publishing."));
                return new AssemblyInstrumentationResult(asset.RelativePath, false, false, false, false, null, [.. diagnostics]);
            }

            readyToRunStripped = true;
            diagnostics.Add(RewriteDiagnostic.Info(
                RewriteDiagnosticIds.ReadyToRunStripped,
                $"'{asset.RelativePath}' is ReadyToRun; the native image is stripped and IL-only output is produced."));
            engineInput = outputPath + ".r2rstrip.tmp";
            ReadyToRunStripper.StripToIL(inputPath, engineInput);
        }
        else if (image.IsMixedMode)
        {
            diagnostics.Add(RewriteDiagnostic.Error(
                RewriteDiagnosticIds.MixedModeAssembly,
                $"'{asset.RelativePath}' is a mixed-mode assembly, which cannot be rewritten. Exclude it or build a pure-IL variant."));
            return new AssemblyInstrumentationResult(asset.RelativePath, false, false, false, false, null, [.. diagnostics]);
        }

        if (image.HasAuthenticodeSignature)
        {
            diagnostics.Add(RewriteDiagnostic.Warning(
                RewriteDiagnosticIds.AuthenticodeDropped,
                $"'{asset.RelativePath}' carries an Authenticode signature, which cannot be preserved across a rewrite and is dropped. Re-sign the staged output out of band if required."));
        }

        StrongNameInfo strongName = StrongNameInspector.Inspect(inputPath);
        bool willReSign = false;
        if (strongName.HasPublicKey)
        {
            if (configuration.StrongNamePolicy == StrongNamePolicy.Fail)
            {
                diagnostics.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.StrongNameReSignRequired,
                    $"'{asset.RelativePath}' is strong-named ({strongName.Status}, token {strongName.PublicKeyToken}). Rewriting invalidates the signature. Set strongNamePolicy to 'ReSign' and supply a signing key."));
                TryDeleteTemp(engineInput, inputPath);
                return new AssemblyInstrumentationResult(asset.RelativePath, false, false, false, readyToRunStripped, null, [.. diagnostics]);
            }

            if (key is null)
            {
                diagnostics.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.StrongNameReSignRequired,
                    $"'{asset.RelativePath}' is strong-named but no usable signing key is available for re-signing."));
                TryDeleteTemp(engineInput, inputPath);
                return new AssemblyInstrumentationResult(asset.RelativePath, false, false, false, readyToRunStripped, null, [.. diagnostics]);
            }

            willReSign = true;
        }

        RewriteResult rewrite = RewriteEngine.Rewrite(new RewriteRequest(engineInput, outputPath, ruleSet, options));
        diagnostics.AddRange(rewrite.Diagnostics);
        TryDeleteTemp(engineInput, inputPath);

        if (!rewrite.Succeeded)
        {
            return new AssemblyInstrumentationResult(
                asset.RelativePath, rewrite.WasWritten, rewrite.WasNoOp, false, readyToRunStripped, rewrite.Manifest, [.. diagnostics]);
        }

        bool wasReSigned = false;
        if (willReSign && key is not null && File.Exists(outputPath))
        {
            try
            {
                StrongNameSigner.ReSign(outputPath, key);
                wasReSigned = true;
                diagnostics.Add(RewriteDiagnostic.Info(
                    RewriteDiagnosticIds.StrongNameReSigned,
                    $"'{asset.RelativePath}' was re-signed with the configured strong-name key."));
            }
            catch (SigningException ex)
            {
                diagnostics.Add(RewriteDiagnostic.Error(
                    RewriteDiagnosticIds.StrongNameReSignRequired,
                    $"Failed to re-sign '{asset.RelativePath}': {ex.Message}"));
            }
        }

        return new AssemblyInstrumentationResult(
            asset.RelativePath, rewrite.WasWritten, rewrite.WasNoOp, wasReSigned, readyToRunStripped, rewrite.Manifest, [.. diagnostics]);
    }

    private static ImmutableArray<string> ResolveReplacementAssemblies(
        string sourceDirectory,
        Rules.RewriteRuleSet ruleSet,
        InstrumentationConfiguration configuration)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string name in ruleSet.Rules
            .Select(r => r.Replacement.AssemblyName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(sourceDirectory, name + ".dll");
            if (File.Exists(candidate))
            {
                paths.Add(candidate);
            }

            if (configuration.Mode == InstrumentationMode.RaceExploration)
            {
                paths.Add(typeof(Runtime.Racing.RaceInstrumentation).Assembly.Location);
            }
        }

        return [.. paths];
    }

    private static HashSet<string> ResolveReplacementNames(
        Rules.RewriteRuleSet ruleSet,
        InstrumentationConfiguration configuration)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ruleSet.Rules
            .Select(r => r.Replacement.AssemblyName)
            .Where(n => !string.IsNullOrEmpty(n)))
        {
            names.Add(name);
        }

        if (configuration.Mode == InstrumentationMode.RaceExploration)
        {
            names.Add("Clockwork.Runtime");
        }

        return names;
    }
    private static ClosureManifest BuildManifest(
        ClosurePlan plan,
        InstrumentationConfiguration configuration,
        Rules.RewriteRuleSet ruleSet,
        string incrementalKey,
        IReadOnlyList<AssemblyInstrumentationResult> assemblyResults,
        IReadOnlyList<string> copied)
    {
        var entries = assemblyResults.Select(a => new ClosureManifestEntry(
            a.RelativePath,
            a.WasRewritten,
            a.WasNoOp,
            a.WasReSigned,
            a.ReadyToRunStripped,
            a.Manifest?.Input.Sha256,
            a.Manifest?.Output?.Sha256,
            a.Errors.Count())).ToImmutableArray();

        return new ClosureManifest
        {
            EngineVersion = RewriteEngine.EngineVersion,
            RuleSetId = ruleSet.Id,
            RuleSetVersion = ruleSet.Version,
            RuleSetSignature = ruleSet.ComputeSignature(),
            ConfigurationSignature = configuration.ComputeSignature(),
            Mode = configuration.Mode,
            IncrementalKey = incrementalKey,
            EntryRelativePath = plan.EntryAssemblyRelativePath,
            Assemblies = entries,
            CopiedAssets = [.. copied],
        };
    }

    private static string ComputeIncrementalKey(
        ClosurePlan plan,
        InstrumentationConfiguration configuration,
        Rules.RewriteRuleSet ruleSet,
        StrongNameKey? key)
    {
        var canonical = new StringBuilder();
        canonical.Append("engine=").Append(RewriteEngine.EngineVersion).Append('\n');
        canonical.Append("config=").Append(configuration.ComputeSignature()).Append('\n');
        canonical.Append("ruleset=").Append(ruleSet.Id).Append('/').Append(ruleSet.Version)
            .Append('/').Append(ruleSet.ComputeSignature()).Append('\n');
        canonical.Append("r2r=").Append(configuration.ReadyToRunPolicy).Append('\n');
        canonical.Append("sn=").Append(configuration.StrongNamePolicy).Append('\n');
        canonical.Append("key=").Append(key is null ? "none" : HashBytes(key.Blob)).Append('\n');
        if (configuration.Mode == InstrumentationMode.RaceExploration)
        {
            canonical.Append("raceRuntime=")
                .Append(TryHashFile(typeof(Runtime.Racing.RaceInstrumentation).Assembly.Location) ?? "missing")
                .Append('\n');
        }

        foreach (ClosureAsset asset in plan.Assets)
        {
            canonical.Append("asset=").Append(asset.RelativePath)
                .Append(':').Append(asset.Rewrite ? 'R' : 'C')
                .Append(':').Append(TryHashFile(asset.SourcePath) ?? "missing")
                .Append('\n');
        }

        return HashString(canonical.ToString());
    }

    private static void PrepareStagingDirectory(string stagingDirectory)
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);
    }

    private static void ValidateStagingDirectory(string sourceDirectory, string stagingDirectory)
    {
        string source = NormalizeDirectoryPath(sourceDirectory);
        string staging = NormalizeDirectoryPath(stagingDirectory);
        string root = NormalizeDirectoryPath(Path.GetPathRoot(stagingDirectory)
            ?? throw new ClosureException($"Staging directory '{stagingDirectory}' has no filesystem root."));

        if (string.Equals(staging, root, PathComparison)
            || IsSameOrDescendant(source, staging)
            || IsSameOrDescendant(staging, source))
        {
            throw new ClosureException(
                $"Staging directory '{stagingDirectory}' must be a dedicated directory which does not equal, contain, or reside within source directory '{sourceDirectory}'.");
        }
    }

    private static bool IsSameOrDescendant(string candidate, string parent) =>
        string.Equals(candidate, parent, PathComparison)
        || candidate.StartsWith(parent + Path.DirectorySeparatorChar, PathComparison);

    private static string NormalizeDirectoryPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void WriteAllTextAtomically(string path, string contents)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ToStagingPath(string stagingDirectory, string relativePath) =>
        Path.Combine(stagingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string? TryHashFile(string path)
    {
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryDeleteTemp(string candidate, string original)
    {
        if (string.Equals(candidate, original, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
        catch (IOException)
        {
        }
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string HashString(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
