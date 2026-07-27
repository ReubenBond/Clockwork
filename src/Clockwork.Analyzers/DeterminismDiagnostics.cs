using Microsoft.CodeAnalysis;

namespace Clockwork.Analyzers;

/// <summary>
/// The diagnostic descriptors raised by <see cref="NondeterministicApiAnalyzer"/>. They deliberately
/// mirror the two policy classes of the built-in <c>clockwork.bcl.deterministic</c> rewrite rule set:
/// <list type="bullet">
///   <item><see cref="ControlledApi"/> (<c>CW1001</c>) - time / identity / random members that the
///   rewriter redirects to a deterministic shim. The call only becomes deterministic once the
///   assembly is instrumented, so this is an informational nudge rather than a defect.</item>
///   <item><see cref="RejectedApi"/> (<c>CW1002</c>) - cryptographic randomness members that draw
///   operating-system entropy. Under an active simulation these are rejected by default and throw
///   unless the explicit test-only deterministic-insecure opt-in is configured.</item>
/// </list>
/// The ids intentionally sit alongside the runtime/rewrite <c>CWR####</c> ids (see
/// <c>RewriteDiagnosticIds</c>) so tooling messages stay coherent across compile time and rewrite time.
/// </summary>
public static class DeterminismDiagnostics
{
    /// <summary>The diagnostic category shared by every Clockwork determinism diagnostic.</summary>
    public const string Category = "Clockwork.Determinism";

    /// <summary>
    /// <c>CW1001</c>: a BCL member which must be rewritten to remain under Clockwork's simulation
    /// control when the containing assembly is instrumented.
    /// </summary>
    public static readonly DiagnosticDescriptor ControlledApi = new(
        id: "CW1001",
        title: "Nondeterministic BCL member requires Clockwork instrumentation",
        messageFormat: "'{0}' requires Clockwork's built-in '{1}' instrumentation to remain under simulation control",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Nondeterministic BCL, task, thread, and synchronization members are controlled or rejected after the containing assembly is instrumented. Instrumented closure binaries are simulation/test artifacts whose Controlled entry points require an active Clockwork simulation; uninstrumented production binaries retain ordinary BCL behaviour.");

    /// <summary>
    /// <c>CW1002</c>: a cryptographic randomness member that obtains OS entropy and is rejected under
    /// simulation by default.
    /// </summary>
    public static readonly DiagnosticDescriptor RejectedApi = new(
        id: "CW1002",
        title: "Cryptographic randomness is rejected under simulation by default",
        messageFormat: "'{0}' obtains operating-system entropy and is rejected under simulation by default; it throws unless the test-only deterministic-insecure crypto opt-in is enabled",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Cryptographic randomness cannot be made deterministic without weakening security. Under an active simulation these members are rejected with a precise diagnostic. A test-only opt-in can substitute deterministic-insecure bytes; production security semantics are never changed.");
}
