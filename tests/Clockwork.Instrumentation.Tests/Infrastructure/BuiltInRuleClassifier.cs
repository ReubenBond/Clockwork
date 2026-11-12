using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Runtime.Policy;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Infrastructure;

/// <summary>How the rewriter treats a call to a reference-pack member under every built-in rule.</summary>
internal enum RuleOutcome
{
    /// <summary>No rule rewrites the call.</summary>
    None,

    /// <summary>A rule redirects or wraps the call.</summary>
    Controlled,

    /// <summary>A rule rejects the call.</summary>
    Rejected,

    /// <summary>The declaring type is substituted, and its substitute declares the member.</summary>
    Substituted,

    /// <summary>The member-aware pass rebuilds the awaiter the call returns.</summary>
    AwaiterSource,
}

/// <summary>The outcome of a call to a member, with the rule that decides it.</summary>
/// <param name="Outcome">How the rewriter treats the call.</param>
/// <param name="RuleId">The id of the deciding rule, if any.</param>
internal readonly record struct RuleClassification(RuleOutcome Outcome, string? RuleId)
{
    public override string ToString() => RuleId is null ? Outcome.ToString() : $"{Outcome} by {RuleId}";
}

/// <summary>
/// Classifies reference-pack members with the rewriter's own rule matcher and substitution mapper over every built-in
/// rule, so the classification agrees with what a rewrite does. Like <see cref="ReferencePack"/>, it is shared by the
/// tests of <see cref="ReferencePackTests"/> and lives for the test run.
/// </summary>
internal sealed class BuiltInRuleClassifier
{
    private static readonly Lazy<BuiltInRuleClassifier> Instance = new(() => new BuiltInRuleClassifier(ReferencePack.Shared));

    private static readonly HashSet<string> AwaiterSourceReceivers = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
    };

    private readonly RewriteRuleMatcher _matcher;
    private readonly MemberSubstitutionMapper _mapper;

    private BuiltInRuleClassifier(ReferencePack pack)
    {
        Rules = [.. BuiltInRuleSets.DeterministicBclInventory.Concat(BuiltInRuleSets.ControlledTasksInventory).Select(entry => entry.Rule)];
        _matcher = new RewriteRuleMatcher(new RewriteRuleSet("clockwork.classification", "1.0.0", Rules), BuiltInRuleSets.SupportedRuntime);
        SubstitutedTypes = _matcher.TypeSubstitutionRules.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        // The mapper imports each substitute into a probe module, which resolves the shim assembly from the test binaries.
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(pack.Directory);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        ModuleDefinition probe = ModuleDefinition.CreateModule(
            "Clockwork.Classification",
            new ModuleParameters { Kind = ModuleKind.Dll, AssemblyResolver = resolver });
        var replacements = new ReplacementResolver(
            [Path.Combine(AppContext.BaseDirectory, BuiltInRuleSets.ShimAssemblyName + ".dll")],
            [pack.Directory]);
        var session = new RewriteSession(probe, _matcher, replacements, new HashSet<string>(StringComparer.Ordinal), []);
        _mapper = MemberSubstitutionMapper.Build(session, (rule, error) => Assert.Fail($"Substitution rule '{rule.Id}' does not resolve: {error}"));
    }

    /// <summary>Gets the classifier over every built-in rule.</summary>
    public static BuiltInRuleClassifier Shared => Instance.Value;

    /// <summary>Gets every built-in rule, in rule-set order.</summary>
    public IReadOnlyList<RewriteRule> Rules { get; }

    /// <summary>Gets the Cecil full names of the substituted types.</summary>
    public IReadOnlySet<string> SubstitutedTypes { get; }

    /// <summary>
    /// Returns whether <paramref name="method"/> is an awaiter source: a task or value task's <c>GetAwaiter</c> or
    /// <c>ConfigureAwait</c>, whose substituted awaiter the member-aware pass rebuilds from the receiver.
    /// </summary>
    public static bool IsAwaiterSource(MethodDefinition method) =>
        method.Name is "GetAwaiter" or "ConfigureAwait" && AwaiterSourceReceivers.Contains(method.DeclaringType.FullName);

    /// <summary>Classifies a call to <paramref name="member"/>.</summary>
    public RuleClassification Classify(IMemberDefinition member)
    {
        string declaringType = member.DeclaringType.FullName;
        if (SubstitutedTypes.Contains(declaringType))
        {
            bool mapped = member switch
            {
                MethodDefinition method => _mapper.MapMethod(method).Kind == MemberMappingKind.Mapped,
                _ => _mapper.OpenSubstituteFor(declaringType)!.Resolve().Fields
                    .Any(field => field.Name == member.Name && ReferencePack.IsVisible(field)),
            };
            return mapped ? new RuleClassification(RuleOutcome.Substituted, _mapper.RuleIdFor(declaringType)) : default;
        }

        if (member is not MethodDefinition invoked)
        {
            return default;
        }

        if (_matcher.TryMatchInvocation(ReferencePack.CallSite(invoked), invoked.IsConstructor, out RewriteRule rule, out _))
        {
            RuleOutcome outcome = rule.Policy == SimulationApiPolicy.Rejected ? RuleOutcome.Rejected : RuleOutcome.Controlled;
            return new RuleClassification(outcome, rule.Id);
        }

        return IsAwaiterSource(invoked) ? new RuleClassification(RuleOutcome.AwaiterSource, null) : default;
    }
}
