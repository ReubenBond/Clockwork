namespace Clockwork.Instrumentation.Manifest;

/// <summary>
/// The outcome recorded for a site the engine considered while applying a rule.
/// </summary>
public enum TransformationOutcome
{
    /// <summary>The site was rewritten (redirected, wrapped, or type-substituted).</summary>
    Transformed,

    /// <summary>A deterministic rejection was injected at the site.</summary>
    Rejected,

    /// <summary>The site matched a pass-through classification and was intentionally left unchanged.</summary>
    PassedThrough,

    /// <summary>The site matched a rule that could not be applied and was skipped per its fallback policy.</summary>
    Skipped,
}
