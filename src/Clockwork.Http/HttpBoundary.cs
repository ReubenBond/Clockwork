namespace Clockwork.Http;

/// <summary>
/// Marker type documenting this project's intended future purpose: an HttpMessageHandler that
/// routes requests through the simulation network (delays, drops, partitions) instead of real
/// sockets. Depends on Clockwork.Runtime. Placeholder for now.
/// </summary>
public static class HttpBoundary
{
    /// <summary>
    /// Anchors a real compile-time and metadata dependency on Clockwork.Runtime, matching the
    /// intended dependency direction, so the reference survives even though no simulated
    /// HttpMessageHandler exists yet.
    /// </summary>
    public static readonly Type RuntimeDependency = typeof(Runtime.RuntimeBoundary);
}
