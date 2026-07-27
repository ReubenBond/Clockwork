namespace Clockwork.Hosting;

/// <summary>
/// Marker type documenting this project's intended future purpose: integration with
/// Microsoft.Extensions.Hosting so that generic-host applications under test can run on the
/// deterministic simulation clock, task scheduler, and synchronization context. Depends on
/// Clockwork.Runtime. Placeholder for now.
/// </summary>
public static class HostingBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Runtime, matching the
    /// intended dependency direction, so the reference survives even though no hosting
    /// integration exists yet.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}
