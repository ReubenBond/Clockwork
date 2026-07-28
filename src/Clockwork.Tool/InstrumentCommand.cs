using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Imaging;
using Clockwork.Instrumentation.Orchestration;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Signing;

namespace Clockwork.Tool;

/// <summary>
/// The <c>instrument</c> command: instruments an application closure from an explicit source directory
/// into an explicit output (staging) directory using the merged rule set, honoring every policy. With
/// <c>--dry-run</c> it discovers the closure and reports the planned per-assembly transformation
/// without writing anything. Output is deterministic text by default or JSON with <c>--json</c>, and
/// the exit code reflects the failure class.
/// </summary>
internal static class InstrumentCommand
{
    public static ExitCode Run(IReadOnlyList<string> args, TextWriter output, TextWriter error)
    {
        var valueOptions = new HashSet<string>(StringComparer.Ordinal)
        {
            "source", "output", "entry", "manifest",
        };
        valueOptions.UnionWith(ConfigurationFactory.ValueOptions);

        ArgumentReader reader = ArgumentReader.Parse(args, valueOptions);
        string? source = reader.GetString("source") ?? FirstPositional(reader);
        string? outputDir = reader.GetString("output");
        string? entry = reader.GetString("entry");
        string? manifest = reader.GetString("manifest");
        bool json = reader.GetFlag("json");
        bool dryRun = reader.GetFlag("dry-run");

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new UsageException("The 'instrument' command requires '--source <directory>'.");
        }

        InstrumentationConfiguration configuration = ConfigurationFactory.Build(reader);
        RewriteRuleSet ruleSet = RuleSetMerge.LoadAndMerge(configuration).RuleSet;
        reader.EnsureAllConsumed();

        if (dryRun)
        {
            return DryRun(source, configuration, ruleSet, entry, json, output);
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            throw new UsageException("The 'instrument' command requires '--output <directory>' unless '--dry-run' is set.");
        }

        var request = new InstrumentationRequest
        {
            SourceDirectory = source,
            StagingDirectory = outputDir,
            Configuration = configuration,
            RuleSet = ruleSet,
            EntryAssemblyName = string.IsNullOrWhiteSpace(entry) ? null : entry,
        };

        if (!string.IsNullOrWhiteSpace(manifest))
        {
            request = request with { ManifestPath = manifest };
        }

        InstrumentationResult result = InstrumentationRunner.Run(request);
        if (json)
        {
            output.WriteLine(WriteResultJson(result));
        }
        else
        {
            WriteResultText(result, output);
        }

        return result.Succeeded ? ExitCode.Success : ExitCode.InstrumentationError;
    }

    private static ExitCode DryRun(
        string source,
        InstrumentationConfiguration configuration,
        RewriteRuleSet ruleSet,
        string? entry,
        bool json,
        TextWriter output)
    {
        ClosurePlan plan = ClosureDiscovery.Discover(source, configuration, string.IsNullOrWhiteSpace(entry) ? null : entry);
        StrongNameKey? key = LoadKey(configuration);

        var rows = new List<PlannedAction>();
        foreach (ClosureAsset asset in plan.AssembliesToRewrite)
        {
            rows.Add(PlanFor(asset, configuration, key));
        }

        bool anyBlocking = rows.Any(r => r.IsBlocking);

        // The merged rule set carries a synthesized 'clockwork.merged' id once more than one built-in
        // (or document) contributes, so surface the constituent built-in ids for an auditable dry run.
        ImmutableArray<string> builtInIds = [.. RuleSetMerge.ResolveBuiltIns(configuration).Select(s => s.Id)];

        if (json)
        {
            var array = new JsonArray();
            foreach (PlannedAction row in rows.OrderBy(r => r.RelativePath, StringComparer.Ordinal))
            {
                array.Add(new JsonObject
                {
                    ["assembly"] = row.RelativePath,
                    ["action"] = row.Action,
                    ["blocking"] = row.IsBlocking,
                });
            }

            var builtInArray = new JsonArray();
            foreach (string id in builtInIds)
            {
                builtInArray.Add(id);
            }

            var doc = new JsonObject
            {
                ["command"] = "instrument",
                ["dryRun"] = true,
                ["source"] = plan.RootDirectory,
                ["ruleSetId"] = ruleSet.Id,
                ["ruleSetVersion"] = ruleSet.Version,
                ["ruleCount"] = ruleSet.Rules.Length,
                ["builtInRuleSets"] = builtInArray,
                ["candidates"] = rows.Count,
                ["copies"] = plan.AssetsToCopy.Count(),
                ["blocking"] = anyBlocking,
                ["plan"] = array,
            };
            output.WriteLine(doc.ToJsonString(Indented));
        }
        else
        {
            output.WriteLine($"Dry run for '{plan.RootDirectory}'");
            output.WriteLine($"Rule set: {ruleSet.Id} v{ruleSet.Version} ({ruleSet.Rules.Length} rules)");
            if (!builtInIds.IsEmpty)
            {
                output.WriteLine($"Built-in rule sets: {string.Join(", ", builtInIds)}");
            }

            output.WriteLine($"Instrumentation candidates: {rows.Count}; verbatim copies: {plan.AssetsToCopy.Count()}");
            foreach (PlannedAction row in rows.OrderBy(r => r.RelativePath, StringComparer.Ordinal))
            {
                output.WriteLine($"  {(row.IsBlocking ? "BLOCK" : "ok   ")} {row.RelativePath}: {row.Action}");
            }

            output.WriteLine(anyBlocking
                ? "One or more assemblies would fail; nothing was written."
                : "All candidates can be instrumented; nothing was written.");
        }

        return anyBlocking ? ExitCode.InstrumentationError : ExitCode.Success;
    }

    private static PlannedAction PlanFor(ClosureAsset asset, InstrumentationConfiguration configuration, StrongNameKey? key)
    {
        AssemblyImageInfo image;
        try
        {
            image = AssemblyImageInfo.Inspect(asset.SourcePath);
        }
        catch (BadImageFormatException)
        {
            return new PlannedAction(asset.RelativePath, "skip (not a managed assembly)", IsBlocking: false);
        }

        if (image.IsReadyToRun)
        {
            return configuration.ReadyToRunPolicy == ReadyToRunPolicy.Reject
                ? new PlannedAction(asset.RelativePath, "reject (ReadyToRun; policy Reject)", IsBlocking: true)
                : new PlannedAction(asset.RelativePath, "strip ReadyToRun to IL, then instrument", IsBlocking: false);
        }

        if (image.IsMixedMode)
        {
            return new PlannedAction(asset.RelativePath, "fail (mixed-mode; not round-trippable)", IsBlocking: true);
        }

        StrongNameInfo strongName = StrongNameInspector.Inspect(asset.SourcePath);
        if (strongName.Status != StrongNameStatus.None)
        {
            if (configuration.StrongNamePolicy == StrongNamePolicy.Fail)
            {
                return new PlannedAction(asset.RelativePath, "fail (strong-named; re-signing required but policy is Fail)", IsBlocking: true);
            }

            if (key is null || !key.CanSign)
            {
                return new PlannedAction(asset.RelativePath, "fail (strong-named; re-signing requires a usable key)", IsBlocking: true);
            }

            return new PlannedAction(asset.RelativePath, "instrument and re-sign", IsBlocking: false);
        }

        return new PlannedAction(asset.RelativePath, "instrument", IsBlocking: false);
    }

    private static StrongNameKey? LoadKey(InstrumentationConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.StrongNameKeyPath))
        {
            return null;
        }

        try
        {
            return StrongNameKey.Load(configuration.StrongNameKeyPath);
        }
        catch (SigningException)
        {
            return null;
        }
    }

    private static void WriteResultText(InstrumentationResult result, TextWriter output)
    {
        output.WriteLine(result.WasIncrementalHit
            ? $"Up to date (incremental): '{result.StagingDirectory}'"
            : $"Instrumented '{result.StagingDirectory}'");
        output.WriteLine($"Manifest: {result.ManifestPath}");
        output.WriteLine($"Instrumented: {result.RewrittenCount}; no-ops: {result.NoOpCount}; copied assets: {result.CopiedAssets.Length}");
        foreach (AssemblyInstrumentationResult assembly in result.Assemblies.OrderBy(a => a.RelativePath, StringComparer.Ordinal))
        {
            foreach (RewriteDiagnostic diagnostic in assembly.Diagnostics)
            {
                output.WriteLine($"  {assembly.RelativePath}: {diagnostic.Id} [{diagnostic.Severity}] {diagnostic.Message}");
            }
        }

        foreach (RewriteDiagnostic diagnostic in result.Diagnostics)
        {
            output.WriteLine($"  {diagnostic.Id} [{diagnostic.Severity}] {diagnostic.Message}");
        }

        output.WriteLine(result.Succeeded ? "Result: success" : "Result: failed");
    }

    private static string WriteResultJson(InstrumentationResult result)
    {
        var assemblies = new JsonArray();
        foreach (AssemblyInstrumentationResult assembly in result.Assemblies.OrderBy(a => a.RelativePath, StringComparer.Ordinal))
        {
            assemblies.Add(new JsonObject
            {
                ["assembly"] = assembly.RelativePath,
                ["instrumented"] = assembly.WasRewritten,
                ["noOp"] = assembly.WasNoOp,
                ["reSigned"] = assembly.WasReSigned,
                ["readyToRunStripped"] = assembly.ReadyToRunStripped,
                ["diagnostics"] = DiagnosticsJson(assembly.Diagnostics),
            });
        }

        var doc = new JsonObject
        {
            ["command"] = "instrument",
            ["succeeded"] = result.Succeeded,
            ["incremental"] = result.WasIncrementalHit,
            ["stagingDirectory"] = result.StagingDirectory,
            ["manifestPath"] = result.ManifestPath,
            ["instrumentedCount"] = result.RewrittenCount,
            ["noOpCount"] = result.NoOpCount,
            ["copiedAssets"] = result.CopiedAssets.Length,
            ["assemblies"] = assemblies,
            ["diagnostics"] = DiagnosticsJson(result.Diagnostics),
        };
        return doc.ToJsonString(Indented);
    }

    private static JsonArray DiagnosticsJson(ImmutableArray<RewriteDiagnostic> diagnostics)
    {
        var array = new JsonArray();
        foreach (RewriteDiagnostic diagnostic in diagnostics)
        {
            array.Add(new JsonObject
            {
                ["id"] = diagnostic.Id,
                ["severity"] = diagnostic.Severity.ToString(),
                ["message"] = diagnostic.Message,
            });
        }

        return array;
    }

    private static string? FirstPositional(ArgumentReader reader) =>
        reader.Positional.Count > 0 ? reader.Positional[0] : null;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly record struct PlannedAction(string RelativePath, string Action, bool IsBlocking);
}
