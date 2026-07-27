namespace Clockwork.Instrumentation.Build;

/// <summary>
/// Marker type documenting this project's intended future purpose: an MSBuild task package that
/// rewrites compiled assemblies at build time using Clockwork.Instrumentation. Placeholder for
/// now - no MSBuild task or IL rewriting logic yet. See docs/compatibility.md for the roadmap and
/// the deferred Mono.Cecil dependency.
/// </summary>
public static class InstrumentationBuildBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Instrumentation, matching
    /// the intended dependency direction, so the reference survives even though no MSBuild task
    /// exists yet.
    /// </summary>
    public static readonly Type InstrumentationDependency = typeof(Instrumentation.InstrumentationBoundary);
}
