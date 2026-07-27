namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// How the instrumentation pipeline treats an input assembly that carries a strong name. Rewriting
/// the IL invalidates the existing strong-name signature, so the pipeline must be told, explicitly,
/// what to do rather than silently emitting an assembly whose signature no longer verifies.
/// </summary>
public enum StrongNamePolicy
{
    /// <summary>
    /// Refuse to rewrite a strong-named assembly and fail with a clear error unless a re-signing key
    /// is supplied. This is the safe default: it never emits a strong-named assembly with an invalid
    /// signature.
    /// </summary>
    Fail,

    /// <summary>
    /// Re-sign the rewritten assembly with a supplied strong-name key
    /// (<see cref="InstrumentationConfiguration.StrongNameKeyPath"/>). Fails clearly if no key is
    /// available or the runtime/Cecil cannot perform the signing.
    /// </summary>
    ReSign,
}
