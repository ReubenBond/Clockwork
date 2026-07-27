namespace Clockwork.Instrumentation.Configuration;

/// <summary>
/// How the instrumentation pipeline treats an input assembly that is compiled as ReadyToRun (R2R) -
/// i.e. that carries an ahead-of-time native code image alongside its IL. Because Mono.Cecil only
/// round-trips the managed IL, silently writing an R2R input back would either strip the native code
/// without saying so or leave stale native code that does not match the rewritten IL; both are
/// unacceptable, so the policy must be explicit.
/// </summary>
public enum ReadyToRunPolicy
{
    /// <summary>
    /// Refuse to rewrite an R2R assembly and fail with a clear error. This is the safe default: it
    /// never emits a mismatched or stale native image. Callers should feed IL-only inputs (rewrite
    /// before the R2R/crossgen publish step) instead.
    /// </summary>
    Reject,

    /// <summary>
    /// Rewrite the managed IL and emit an <b>IL-only</b> staged assembly, discarding the precompiled
    /// native image so the runtime JITs the rewritten IL. Only the managed code is preserved; the
    /// AOT native code is intentionally dropped. Use only when re-JITing from IL is acceptable.
    /// </summary>
    StripToIL,
}
