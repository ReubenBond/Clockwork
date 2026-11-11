namespace Clockwork.Instrumentation.Rules;

/// <summary>
/// An inclusive range of target-runtime versions a <see cref="RewriteRule"/> supports. A rule only
/// applies when the engine's configured target-runtime version falls within the range. Either bound
/// may be open (<see langword="null"/>). The default <see cref="All"/> matches every version.
/// </summary>
/// <param name="Minimum">The inclusive lower bound, or <see langword="null"/> for no lower bound.</param>
/// <param name="Maximum">The inclusive upper bound, or <see langword="null"/> for no upper bound.</param>
public readonly record struct RuntimeVersionRange(Version? Minimum, Version? Maximum)
{
    /// <summary>Gets a range that includes every version.</summary>
    public static RuntimeVersionRange All => default;

    /// <summary>Creates a range with only a lower bound.</summary>
    public static RuntimeVersionRange AtLeast(Version minimum) => new(minimum, null);

    /// <summary>Creates a range with only an upper bound.</summary>
    public static RuntimeVersionRange AtMost(Version maximum) => new(null, maximum);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="version"/> falls within this range. A
    /// <see langword="null"/> version (unspecified target runtime) is considered in range.
    /// </summary>
    public bool Includes(Version? version)
    {
        if (version is null)
        {
            return true;
        }

        if (Minimum is not null && version < Minimum)
        {
            return false;
        }

        if (Maximum is not null && version > Maximum)
        {
            return false;
        }

        return true;
    }

    /// <summary>Returns a stable canonical string for signature hashing.</summary>
    public string ToCanonicalString()
    {
        var canonical = new CanonicalEncoding(nameof(RuntimeVersionRange));
        canonical.AddString(nameof(Minimum), Minimum?.ToString());
        canonical.AddString(nameof(Maximum), Maximum?.ToString());
        return canonical.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => ToCanonicalString();
}
