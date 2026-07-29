using System.Collections.Immutable;
using System.Text.Json;
using Clockwork.Instrumentation.Inspection;
using Clockwork.Instrumentation.Orchestration;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Clockwork.Instrumentation.Build;

/// <summary>
/// Verifies that every managed assembly selected from an instrumented test closure was successfully
/// rewritten and that its staged entry assembly is runnable.
/// </summary>
public sealed class ClockworkValidateInstrumentedTestTask : MSBuildTask
{
    /// <summary>Gets or sets the staged instrumented closure directory.</summary>
    [Required]
    public string StagingDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets the closure manifest path.</summary>
    [Required]
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected closure-relative entry assembly path.</summary>
    [Required]
    public string EntryAssemblyName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool Execute()
    {
        ParsedManifest manifest;
        try
        {
            manifest = ReadManifest(ManifestPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Log.LogError(
                null,
                "CWR0202",
                null,
                ManifestPath,
                0,
                0,
                0,
                0,
                $"Clockwork could not validate the instrumented test manifest: {exception.Message}");
            return false;
        }

        string stagingRoot = Path.GetFullPath(StagingDirectory);
        string expectedEntry = NormalizeRelative(
            EntryAssemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? EntryAssemblyName
                : EntryAssemblyName + ".dll");
        if (!string.Equals(manifest.EntryRelativePath, expectedEntry, StringComparison.Ordinal))
        {
            LogError(
                "CWR0203",
                $"Instrumented test manifest entry '{manifest.EntryRelativePath ?? "<none>"}' does not match '{expectedEntry}'.");
        }

        foreach (ClosureManifestEntry entry in manifest.Assemblies)
        {
            if ((!entry.WasRewritten && !entry.WasNoOp) || entry.ErrorCount != 0)
            {
                LogError(
                    "CWR0204",
                    $"Assembly '{entry.RelativePath}' was not successfully instrumented according to '{ManifestPath}'.");
                continue;
            }

            if (!TryResolveWithinRoot(stagingRoot, entry.RelativePath, out string stagedPath))
            {
                LogError("CWR0205", $"Manifest assembly path '{entry.RelativePath}' escapes the staged closure.");
                continue;
            }

            if (!File.Exists(stagedPath))
            {
                LogError("CWR0206", $"Staged test assembly '{stagedPath}' was not found.");
                continue;
            }

            if (!AssemblyInspector.TryReadMarker(stagedPath, out _))
            {
                LogError(
                    "CWR0207",
                    $"Staged test assembly '{stagedPath}' does not carry a Clockwork rewrite signature.");
            }
        }

        bool entryRewritten = manifest.Assemblies.Any(entry =>
            string.Equals(entry.RelativePath, expectedEntry, StringComparison.Ordinal));
        bool entryCopied = manifest.CopiedAssets.Any(entry =>
            string.Equals(entry, expectedEntry, StringComparison.Ordinal));
        if (!entryRewritten && !entryCopied)
        {
            LogError("CWR0208", $"Entry assembly '{expectedEntry}' was neither instrumented nor copied.");
        }

        return !Log.HasLoggedErrors;
    }

    private static ParsedManifest ReadManifest(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out JsonElement schema) ||
            !schema.TryGetInt32(out int schemaVersion) ||
            schemaVersion != ClosureManifest.SchemaVersion ||
            !root.TryGetProperty("assemblies", out JsonElement assemblies) ||
            assemblies.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("copiedAssets", out JsonElement copiedAssets) ||
            copiedAssets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The closure manifest has an unsupported or invalid shape.");
        }

        string? entryRelativePath = root.TryGetProperty("entryRelativePath", out JsonElement entry) &&
            entry.ValueKind == JsonValueKind.String
                ? entry.GetString()
                : null;
        var entries = ImmutableArray.CreateBuilder<ClosureManifestEntry>();
        foreach (JsonElement item in assemblies.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("relativePath", out JsonElement relativePath) ||
                relativePath.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("wasRewritten", out JsonElement wasRewritten) ||
                wasRewritten.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !item.TryGetProperty("wasNoOp", out JsonElement wasNoOp) ||
                wasNoOp.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !item.TryGetProperty("errorCount", out JsonElement errorCount) ||
                !errorCount.TryGetInt32(out int errors))
            {
                throw new InvalidDataException("The closure manifest contains an invalid assembly entry.");
            }

            entries.Add(new ClosureManifestEntry(
                relativePath.GetString()!,
                wasRewritten.GetBoolean(),
                wasNoOp.GetBoolean(),
                WasReSigned: false,
                ReadyToRunStripped: false,
                InputSha256: null,
                OutputSha256: null,
                errors));
        }

        var copied = ImmutableArray.CreateBuilder<string>();
        foreach (JsonElement item in copiedAssets.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The closure manifest contains an invalid copied asset.");
            }

            copied.Add(item.GetString()!);
        }

        return new ParsedManifest(entryRelativePath, entries.ToImmutable(), copied.ToImmutable());
    }

    private void LogError(string code, string message) =>
        Log.LogError(null, code, null, null, 0, 0, 0, 0, message);

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool TryResolveWithinRoot(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private sealed record ParsedManifest(
        string? EntryRelativePath,
        ImmutableArray<ClosureManifestEntry> Assemblies,
        ImmutableArray<string> CopiedAssets);
}
