namespace Clockwork.Instrumentation;

/// <summary>
/// Marker type documenting this project's intended future purpose: contracts and runtime hooks
/// for deterministic instrumentation (e.g., interception points used by cooperative, controlled,
/// or race-exploration execution modes). Depends on Clockwork.Runtime. Does not use Mono.Cecil or
/// perform any IL rewriting yet - that belongs to a later phase and, when added, to
/// Clockwork.Instrumentation.Build. See docs/compatibility.md for the overall roadmap.
/// </summary>
public static class InstrumentationBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Runtime, matching the
    /// intended dependency direction, so the reference survives even though no runtime behavior
    /// exists yet.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}
