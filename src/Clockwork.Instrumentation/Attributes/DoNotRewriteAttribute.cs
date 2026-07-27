namespace Clockwork.Instrumentation.Attributes;

/// <summary>
/// Marks a type that <see cref="Rewriting.RewriteEngine"/> must never rewrite. The engine skips the
/// whole type (and its nested members) when this attribute is present, and records the skip as an
/// explicit exclusion in the instrumentation manifest rather than silently ignoring it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class DoNotRewriteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DoNotRewriteAttribute"/> class.
    /// </summary>
    /// <param name="reason">A human-readable reason recorded as the exclusion's justification.</param>
    public DoNotRewriteAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Gets the reason this type is excluded from rewriting.</summary>
    public string Reason { get; }
}
