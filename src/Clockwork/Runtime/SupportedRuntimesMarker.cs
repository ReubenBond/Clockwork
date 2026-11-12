using System.Reflection;
using System.Runtime.CompilerServices;

namespace Clockwork.Runtime;

/// <summary>
/// Records on each assembly Clockwork rewrites the .NET runtimes it supports, and checks at simulation start that the
/// process runs one of them. The marker is an <see cref="AssemblyMetadataAttribute"/> keyed <see cref="Key"/> whose
/// value is <c>[minimum,maximum]</c>: inclusive bounds, each a feature version (major.minor) or empty when open.
/// </summary>
internal static class SupportedRuntimesMarker
{
    /// <summary>The <see cref="AssemblyMetadataAttribute"/> key of the marker.</summary>
    public const string Key = "Clockwork.SupportedRuntimes";

    private static readonly string ClockworkAssemblyName = typeof(SupportedRuntimesMarker).Assembly.GetName().Name!;
    private static readonly ConditionalWeakTable<Assembly, object> Checked = new();
    private static readonly object CheckedValue = new();

    /// <summary>Formats the marker value for an inclusive range of feature versions.</summary>
    /// <param name="minimum">The lower bound, or <see langword="null"/> when open.</param>
    /// <param name="maximum">The upper bound, or <see langword="null"/> when open.</param>
    /// <returns>The marker value.</returns>
    public static string Format(Version? minimum, Version? maximum)
    {
        RequireFeatureVersion(minimum, nameof(minimum));
        RequireFeatureVersion(maximum, nameof(maximum));
        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            throw new ArgumentException($"The minimum {minimum} exceeds the maximum {maximum}.", nameof(minimum));
        }

        return $"[{minimum},{maximum}]";
    }

    /// <summary>Parses a marker value.</summary>
    /// <param name="value">The marker value.</param>
    /// <param name="minimum">The lower bound, or <see langword="null"/> when open.</param>
    /// <param name="maximum">The upper bound, or <see langword="null"/> when open.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a well-formed marker value.</returns>
    public static bool TryParse(string? value, out Version? minimum, out Version? maximum)
    {
        minimum = null;
        maximum = null;
        if (value is not ['[', .. string bounds, ']'])
        {
            return false;
        }

        string[] parts = bounds.Split(',');
        return parts.Length == 2
            && TryParseBound(parts[0], out minimum)
            && TryParseBound(parts[1], out maximum)
            && !(minimum is not null && maximum is not null && minimum > maximum);
    }

    /// <summary>
    /// Requires every loaded assembly Clockwork rewrote to support the runtime this process runs. Each assembly is
    /// checked once per process; one that fails is checked again at the next simulation start, so it keeps failing.
    /// </summary>
    /// <exception cref="UnsupportedRuntimeException">A rewritten assembly does not support this runtime.</exception>
    public static void VerifyLoadedAssemblies()
    {
        Version runtime = new(Environment.Version.Major, Environment.Version.Minor);
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (Checked.TryGetValue(assembly, out _))
            {
                continue;
            }

            // Every assembly that runs controlled code references Clockwork: one that doesn't runs as written on any
            // runtime, so its attributes need not be read.
            if (!assembly.IsDynamic && ReferencesClockwork(assembly))
            {
                Verify(assembly.GetName().Name!, ReadMarker(assembly), runtime);
            }

            Checked.AddOrUpdate(assembly, CheckedValue);
        }
    }

    /// <summary>Requires an assembly carrying <paramref name="marker"/> to support <paramref name="runtime"/>.</summary>
    /// <param name="assemblyName">The assembly's simple name.</param>
    /// <param name="marker">The assembly's marker value, or <see langword="null"/> if Clockwork did not rewrite it.</param>
    /// <param name="runtime">The feature version of the runtime the process runs.</param>
    internal static void Verify(string assemblyName, string? marker, Version runtime)
    {
        if (marker is null)
        {
            return;
        }

        if (!TryParse(marker, out Version? minimum, out Version? maximum))
        {
            throw new InvalidOperationException(
                $"Assembly '{assemblyName}' carries the Clockwork marker '{Key}' with the unreadable value '{marker}'. " +
                "Instrument it again with this version of Clockwork.");
        }

        if ((minimum is not null && runtime < minimum) || (maximum is not null && runtime > maximum))
        {
            throw new UnsupportedRuntimeException(assemblyName, Describe(minimum, maximum), runtime);
        }
    }

    private static bool ReferencesClockwork(Assembly assembly)
    {
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, ClockworkAssemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadMarker(Assembly assembly)
    {
        foreach (CustomAttributeData attribute in assembly.GetCustomAttributesData())
        {
            if (attribute.AttributeType == typeof(AssemblyMetadataAttribute)
                && attribute.ConstructorArguments is [{ Value: Key }, { Value: var value }])
            {
                return value as string ?? string.Empty;
            }
        }

        return null;
    }

    private static string Describe(Version? minimum, Version? maximum) => (minimum, maximum) switch
    {
        ({ } low, { } high) when low == high => low.ToString(),
        ({ } low, { } high) => $"{low} to {high}",
        ({ } low, null) => $"{low} or later",
        (null, { } high) => $"{high} or earlier",
        _ => "any runtime",
    };

    private static bool TryParseBound(string text, out Version? bound)
    {
        bound = null;
        if (text.Length == 0)
        {
            return true;
        }

        return Version.TryParse(text, out bound) && IsFeatureVersion(bound);
    }

    private static void RequireFeatureVersion(Version? version, string parameterName)
    {
        if (version is not null && !IsFeatureVersion(version))
        {
            throw new ArgumentException($"'{version}' is not a feature version (major.minor).", parameterName);
        }
    }

    private static bool IsFeatureVersion(Version version) => version.Build == -1;
}
