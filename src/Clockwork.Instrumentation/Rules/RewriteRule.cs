using Clockwork.Runtime.Policy;

namespace Clockwork.Instrumentation.Rules;

/// <summary>
/// A single versioned instruction to the rewrite engine: at every site matching <see cref="Target"/>,
/// perform <see cref="Operation"/> using <see cref="Replacement"/>. A rule also carries its simulation
/// policy classification (<see cref="Policy"/>), its <see cref="Fallback"/> behaviour when it cannot
/// be applied, and the range of target runtimes it supports (<see cref="SupportedRuntimes"/>).
/// </summary>
/// <remarks>
/// Rules are pure data and Mono.Cecil-free, so a rule set can be authored, versioned, and hashed
/// independently of the engine. The <see cref="Policy"/> integrates the simulation API-policy model:
/// a <see cref="SimulationApiPolicy.Controlled"/> target is redirected/wrapped, a
/// <see cref="SimulationApiPolicy.Rejected"/> target is rejected, and a
/// <see cref="SimulationApiPolicy.PassThrough"/> target is recorded but left unchanged - without the
/// rule referencing any specific replacement type by identity, keeping the engine decoupled from
/// concrete BCL shims.
/// </remarks>
public sealed record RewriteRule
{
    /// <summary>Gets the stable, unique-within-a-rule-set identity of this rule.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the transformation this rule performs.</summary>
    public required RewriteOperationKind Operation { get; init; }

    /// <summary>Gets the member or type this rule matches.</summary>
    public required MemberSignature Target { get; init; }

    /// <summary>Gets the replacement member or type this rule redirects to.</summary>
    public required RewriteReplacement Replacement { get; init; }

    /// <summary>Gets the simulation API-policy classification of the target. Defaults to <see cref="SimulationApiPolicy.Controlled"/>.</summary>
    public SimulationApiPolicy Policy { get; init; } = SimulationApiPolicy.Controlled;

    /// <summary>Gets the behaviour when this rule matches but cannot be applied. Defaults to <see cref="RewriteFallback.Fail"/>.</summary>
    public RewriteFallback Fallback { get; init; } = RewriteFallback.Fail;

    /// <summary>Gets the range of target runtimes this rule supports. Defaults to <see cref="RuntimeVersionRange.All"/>.</summary>
    public RuntimeVersionRange SupportedRuntimes { get; init; } = RuntimeVersionRange.All;

    /// <summary>Gets an optional human-readable description of the rule's intent.</summary>
    public string? Description { get; init; }

    /// <summary>Creates a rule that redirects a <c>call</c>/<c>callvirt</c> to a static replacement method.</summary>
    public static RewriteRule RedirectCall(string id, MemberSignature target, RewriteReplacement replacement,
        SimulationApiPolicy policy = SimulationApiPolicy.Controlled) =>
        new() { Id = id, Operation = RewriteOperationKind.RedirectCall, Target = target, Replacement = replacement, Policy = policy };

    /// <summary>Creates a rule that redirects a <c>newobj</c> to a static factory method.</summary>
    public static RewriteRule RedirectNewObj(string id, MemberSignature target, RewriteReplacement replacement,
        SimulationApiPolicy policy = SimulationApiPolicy.Controlled) =>
        new() { Id = id, Operation = RewriteOperationKind.RedirectNewObj, Target = target, Replacement = replacement, Policy = policy };

    /// <summary>Creates a rule that substitutes references to a type with another type.</summary>
    public static RewriteRule SubstituteType(string id, string targetTypeFullName, RewriteReplacement replacementType,
        SimulationApiPolicy policy = SimulationApiPolicy.Controlled) =>
        new()
        {
            Id = id,
            Operation = RewriteOperationKind.SubstituteType,
            Target = MemberSignature.Type(targetTypeFullName),
            Replacement = replacementType,
            Policy = policy,
        };

    /// <summary>Creates a rule that inserts a post-call wrapper after a matched call.</summary>
    public static RewriteRule WrapAfterCall(string id, MemberSignature target, RewriteReplacement replacement,
        SimulationApiPolicy policy = SimulationApiPolicy.Controlled) =>
        new() { Id = id, Operation = RewriteOperationKind.WrapAfterCall, Target = target, Replacement = replacement, Policy = policy };

    /// <summary>Creates a rule that injects a deterministic rejection before a matched invocation.</summary>
    public static RewriteRule InjectRejection(string id, MemberSignature target, RewriteReplacement rejectionMethod) =>
        new()
        {
            Id = id,
            Operation = RewriteOperationKind.InjectRejection,
            Target = target,
            Replacement = rejectionMethod,
            Policy = SimulationApiPolicy.Rejected,
        };

    /// <summary>Returns a stable canonical string for signature hashing and diagnostics.</summary>
    public string ToCanonicalString() =>
        $"{Id}|{Operation}|{Target.ToCanonicalString()}|{Replacement.ToCanonicalString()}|{Policy}|{Fallback}|" +
        $"{SupportedRuntimes.ToCanonicalString()}|description:{Description?.Length ?? -1}:{Description}";
}
