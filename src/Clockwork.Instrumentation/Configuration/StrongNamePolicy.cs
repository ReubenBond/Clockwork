namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// Legacy strong-name policy values retained for configuration compatibility. The instrumentation
/// pipeline now strips rewritten strong-name identities and matching closure references automatically.
/// </summary>
public enum StrongNamePolicy
{
    /// <summary>
    /// The legacy fail policy value. Rewritten identities are still stripped automatically.
    /// </summary>
    Fail,

    /// <summary>
    /// The legacy re-sign policy value. Rewritten identities are still stripped automatically.
    /// </summary>
    ReSign,
}
