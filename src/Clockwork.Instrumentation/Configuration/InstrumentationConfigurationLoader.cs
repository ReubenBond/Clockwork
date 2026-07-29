using System.Collections.Immutable;
using System.Text.Json;

namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// Loads and strictly validates an <see cref="InstrumentationConfiguration"/> from a JSON document.
/// Relative rule-set and key paths are resolved against the configuration file's directory so a
/// configuration file is self-contained and portable. Unknown enum values, wrong JSON types, and
/// missing required fields are hard <see cref="ConfigurationException"/>s.
/// </summary>
public static class InstrumentationConfigurationLoader
{
    /// <summary>Loads and validates a configuration from a JSON file, resolving relative paths.</summary>
    /// <param name="path">The configuration file path.</param>
    /// <returns>The validated configuration with resolved absolute paths.</returns>
    /// <exception cref="ConfigurationException">The document is malformed or fails validation.</exception>
    public static InstrumentationConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Configuration file '{path}' was not found.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ConfigurationException($"Configuration file '{path}' could not be read: {ex.Message}", ex);
        }

        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? Directory.GetCurrentDirectory();
        return Parse(json, baseDirectory, path);
    }

    /// <summary>Parses and validates a configuration from a JSON string.</summary>
    /// <param name="json">The configuration text.</param>
    /// <param name="baseDirectory">The directory relative paths are resolved against, or <see langword="null"/> to keep paths as written.</param>
    /// <param name="sourceName">A name for the source used in error messages.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="ConfigurationException">The document is malformed or fails validation.</exception>
    public static InstrumentationConfiguration Parse(string json, string? baseDirectory = null, string? sourceName = null)
    {
        string origin = sourceName is null ? "configuration" : $"configuration '{sourceName}'";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"{origin} is not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationException($"{origin} must be a JSON object.");
            }

            int schema = GetOptionalInt(root, "schemaVersion", origin) ?? InstrumentationConfiguration.CurrentSchemaVersion;
            if (schema != InstrumentationConfiguration.CurrentSchemaVersion)
            {
                throw new ConfigurationException(
                    $"{origin} declares unsupported schemaVersion {schema}; this tool supports version {InstrumentationConfiguration.CurrentSchemaVersion}.");
            }

            ImmutableArray<string> ruleSets = ResolvePaths(GetStringArray(root, "ruleSets", origin), baseDirectory);
            InstrumentationMode mode =
                GetOptionalEnum<InstrumentationMode>(root, "mode", origin) ?? InstrumentationMode.Controlled;
            ImmutableArray<string> builtInRuleSets = GetStringArray(root, "builtInRuleSets", origin);
            ImmutableArray<string> builtInInclude = GetStringArray(root, "builtInIncludeFamilies", origin);
            ImmutableArray<string> builtInExclude = GetStringArray(root, "builtInExcludeFamilies", origin);
            bool strictBuiltIns = GetOptionalBool(root, "strictBuiltIns", origin) ?? true;
            ImmutableArray<string> include = GetStringArray(root, "include", origin);
            ImmutableArray<string> exclude = GetStringArray(root, "exclude", origin);
            bool excludeFramework = GetOptionalBool(root, "excludeFrameworkAssemblies", origin) ?? true;
            bool instrumentDependencies = GetOptionalBool(root, "instrumentDependencies", origin) ?? true;
            Version? targetRuntime = GetOptionalVersion(root, "targetRuntime", origin);
            ReadyToRunPolicy r2r = GetOptionalEnum<ReadyToRunPolicy>(root, "readyToRunPolicy", origin) ?? ReadyToRunPolicy.Reject;
            StrongNamePolicy strongName = GetOptionalEnum<StrongNamePolicy>(root, "strongNamePolicy", origin) ?? StrongNamePolicy.Fail;
            string? keyPath = GetOptionalString(root, "strongNameKeyPath", origin);
            if (keyPath is not null && baseDirectory is not null)
            {
                keyPath = Path.GetFullPath(Path.Combine(baseDirectory, keyPath));
            }

            return new InstrumentationConfiguration
            {
                RuleSetPaths = ruleSets,
                Mode = mode,
                BuiltInRuleSetIds = builtInRuleSets,
                BuiltInIncludeFamilies = builtInInclude,
                BuiltInExcludeFamilies = builtInExclude,
                StrictBuiltIns = strictBuiltIns,
                IncludePatterns = include,
                ExcludePatterns = exclude,
                ExcludeFrameworkAssemblies = excludeFramework,
                InstrumentDependencies = instrumentDependencies,
                TargetRuntime = targetRuntime,
                ReadyToRunPolicy = r2r,
                StrongNamePolicy = strongName,
                StrongNameKeyPath = keyPath,
            };
        }
    }

    private static ImmutableArray<string> ResolvePaths(ImmutableArray<string> paths, string? baseDirectory)
    {
        if (baseDirectory is null || paths.IsDefaultOrEmpty)
        {
            return paths;
        }

        return [.. paths.Select(p => Path.GetFullPath(Path.Combine(baseDirectory, p)))];
    }

    private static ImmutableArray<string> GetStringArray(JsonElement element, string propertyName, string origin)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ConfigurationException($"{origin}: '{propertyName}' must be an array of strings.");
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ConfigurationException($"{origin}: every '{propertyName}' entry must be a string.");
            }

            string text = item.GetString() ?? string.Empty;
            if (text.Length == 0)
            {
                throw new ConfigurationException($"{origin}: '{propertyName}' entries must be non-empty.");
            }

            builder.Add(text);
        }

        return builder.ToImmutable();
    }

    private static string? GetOptionalString(JsonElement element, string propertyName, string origin)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ConfigurationException($"{origin}: '{propertyName}' must be a string.");
        }

        string text = value.GetString() ?? string.Empty;
        return text.Length == 0 ? null : text;
    }

    private static int? GetOptionalInt(JsonElement element, string propertyName, string origin)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
        {
            throw new ConfigurationException($"{origin}: '{propertyName}' must be an integer.");
        }

        return result;
    }

    private static bool? GetOptionalBool(JsonElement element, string propertyName, string origin)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ConfigurationException($"{origin}: '{propertyName}' must be a boolean."),
        };
    }

    private static Version? GetOptionalVersion(JsonElement element, string propertyName, string origin)
    {
        string? text = GetOptionalString(element, propertyName, origin);
        if (text is null)
        {
            return null;
        }

        if (!Version.TryParse(text, out Version? version))
        {
            throw new ConfigurationException($"{origin}: '{propertyName}' value '{text}' is not a valid version.");
        }

        return version;
    }

    private static TEnum? GetOptionalEnum<TEnum>(JsonElement element, string propertyName, string origin)
        where TEnum : struct, Enum
    {
        string? text = GetOptionalString(element, propertyName, origin);
        if (text is null)
        {
            return null;
        }

        if (Enum.TryParse(text, ignoreCase: false, out TEnum result) && Enum.IsDefined(result))
        {
            return result;
        }

        string allowed = string.Join(", ", Enum.GetNames<TEnum>());
        throw new ConfigurationException($"{origin}: '{propertyName}' value '{text}' is not one of: {allowed}.");
    }
}
