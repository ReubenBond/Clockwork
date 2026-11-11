using System.Reflection;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Rules;

/// <summary>
/// Checks the controlled-task rule set against the reference pack this project compiles against. A type substitution
/// rewrites every member reference whose signature mentions the substituted type, but a framework member keeps its
/// declaring type and original signature, so a call to one fails the rewrite with <c>CWR0001</c> unless a rule handles
/// it. Every public or protected member of a non-substituted type whose signature mentions a substituted type must
/// therefore be redirected to a shim, be an awaiter source the member-aware pass rebuilds, or be listed below with the
/// reason it is left to fail closed.
/// </summary>
public sealed class BuiltInRuleSetCompletenessTests
{
    private static readonly Version TargetRuntime = BuiltInRuleSets.TargetRuntime;

    private static readonly HashSet<string> AwaiterSourceReceivers = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
    };

    // Members deliberately left to fail the rewrite with CWR0001, keyed by full name, with the reason. Empty: every
    // reference-pack member that mentions a substituted type is redirected or rebuilt.
    private static readonly Dictionary<string, string> FailClosed = new(StringComparer.Ordinal);

    [Fact]
    public void EveryReferencePackMemberMentioningASubstitutedTypeIsHandled()
    {
        RewriteRuleSet ruleSet = BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies);
        HashSet<string> substituted =
        [
            .. new RewriteRuleMatcher(ruleSet, TargetRuntime).TypeSubstitutionRules.Select(entry => entry.Key),
        ];
        string directory = ReferencePackDirectory();
        var handled = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var unhandled = new List<string>();

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(directory);
        foreach (string path in Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal))
        {
            using ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });

            // A substituted type's own members are mapped onto its substitute.
            foreach (TypeDefinition type in module.GetTypes().Where(type => IsVisible(type) && !substituted.Contains(type.FullName)))
            {
                IEnumerable<MemberReference> members = type.Methods.Where(IsVisible).Cast<MemberReference>()
                    .Concat(type.Fields.Where(IsVisible));
                foreach (MemberReference member in members.Where(member => SignatureMentions.Find(member, substituted).Count > 0))
                {
                    if (member is MethodDefinition method && (IsAwaiterSource(method) || IsRedirected(ruleSet, method)))
                    {
                        handled.Add(member.FullName);
                    }
                    else if (FailClosed.ContainsKey(member.FullName))
                    {
                        allowed.Add(member.FullName);
                    }
                    else
                    {
                        unhandled.Add(member.FullName);
                    }
                }
            }
        }

        Assert.Contains("System.Runtime.CompilerServices.TaskAwaiter System.Threading.Tasks.Task::GetAwaiter()", handled);
        Assert.Contains("System.Runtime.CompilerServices.YieldAwaitable System.Threading.Tasks.Task::Yield()", handled);
        Assert.True(unhandled.Count == 0, "Unhandled members:" + Environment.NewLine + string.Join(Environment.NewLine, unhandled));
        Assert.Empty(FailClosed.Keys.Except(allowed, StringComparer.Ordinal));
    }

    [Fact]
    public void EveryRuleTargetIsAReferencePackMember()
    {
        string directory = ReferencePackDirectory();
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(directory);
        var modules = new List<ModuleDefinition>();
        try
        {
            var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*.dll"))
            {
                ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver });
                modules.Add(module);
                foreach (TypeDefinition type in module.GetTypes().Where(IsVisible))
                {
                    types.TryAdd(type.FullName, type);
                }
            }

            var missing = new List<string>();
            foreach ((_, RewriteRule rule) in BuiltInRuleSets.DeterministicBclInventory.Concat(BuiltInRuleSets.ControlledTasksInventory))
            {
                MemberSignature target = rule.Target;
                if (!types.TryGetValue(target.DeclaringTypeFullName, out TypeDefinition? type))
                {
                    missing.Add($"{rule.Id}: {target.DeclaringTypeFullName}");
                }
                else if (rule.Operation != RewriteOperationKind.SubstituteType
                    && !type.Methods.Any(method => IsVisible(method)
                        && method.Name == target.MemberName
                        && (!target.HasParameterConstraint
                            || target.ParameterTypeFullNames.SequenceEqual(method.Parameters.Select(parameter => CallSiteName(parameter.ParameterType))))))
                {
                    missing.Add($"{rule.Id}: {target.ToCanonicalString()}");
                }
            }

            Assert.True(missing.Count == 0, "Rule targets missing from the reference pack:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }
        finally
        {
            modules.ForEach(module => module.Dispose());
        }
    }

    private static bool IsAwaiterSource(MethodDefinition method) =>
        method.Name is "GetAwaiter" or "ConfigureAwait" && AwaiterSourceReceivers.Contains(method.DeclaringType.FullName);

    // A call-site redirect replaces the reference itself; a wrapper or injected rejection keeps it.
    private static bool IsRedirected(RewriteRuleSet ruleSet, MethodDefinition method) =>
        ruleSet.Rules.Any(rule =>
            (method.IsConstructor ? rule.Operation == RewriteOperationKind.RedirectNewObj : rule.Operation == RewriteOperationKind.RedirectCall)
            && rule.SupportedRuntimes.Includes(TargetRuntime)
            && rule.Target.DeclaringTypeFullName == method.DeclaringType.FullName
            && rule.Target.MemberName == method.Name
            && (!rule.Target.HasParameterConstraint
                || rule.Target.ParameterTypeFullNames.SequenceEqual(method.Parameters.Select(parameter => CallSiteName(parameter.ParameterType)))));

    // The name a call site's member reference gives a parameter type, which is what rule targets match: generic
    // parameters are positional (!0 for the declaring type's, !!0 for the method's).
    private static string CallSiteName(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        GenericInstanceType generic => CallSiteName(generic.ElementType) + "<" + string.Join(",", generic.GenericArguments.Select(CallSiteName)) + ">",
        ByReferenceType byReference => CallSiteName(byReference.ElementType) + "&",
        PointerType pointer => CallSiteName(pointer.ElementType) + "*",
        ArrayType { IsVector: true } array => CallSiteName(array.ElementType) + "[]",
        _ => type.FullName,
    };

    private static bool IsVisible(TypeDefinition type) =>
        type.IsNested
            ? (type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamilyOrAssembly) && IsVisible(type.DeclaringType)
            : type.IsPublic;

    private static bool IsVisible(MethodDefinition method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsVisible(FieldDefinition field) => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static string ReferencePackDirectory()
    {
        string? directory = typeof(BuiltInRuleSetCompletenessTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "ReferencePackDirectory")?.Value;
        Assert.False(string.IsNullOrEmpty(directory), "The test assembly does not record its reference pack directory.");
        Assert.True(File.Exists(Path.Combine(directory, "System.Runtime.dll")), $"No reference pack at '{directory}'.");
        return directory;
    }
}
