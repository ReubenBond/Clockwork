namespace Clockwork.Instrumentation.Rules;

/// <summary>
/// What the engine does when a <see cref="RewriteRule"/> matches a site but cannot be applied there
/// (for example, its replacement member cannot be resolved, or the configured target runtime is
/// outside the rule's supported range). The default is <see cref="Fail"/>: a targeted site is never
/// silently skipped.
/// </summary>
public enum RewriteFallback
{
    /// <summary>Fail the whole rewrite with an error diagnostic. This is the safe default.</summary>
    Fail,

    /// <summary>
    /// Leave the matched site unchanged, recording an explicit warning diagnostic and a
    /// passed-through entry in the manifest. This must be an intentional per-rule opt-in.
    /// </summary>
    Skip,

    /// <summary>
    /// Inject a deterministic rejection call at the matched site instead of the intended
    /// transformation, so the unsupported operation fails loudly at runtime.
    /// </summary>
    Reject,
}
