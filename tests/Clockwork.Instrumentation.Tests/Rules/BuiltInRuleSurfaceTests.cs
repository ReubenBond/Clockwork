using System.Collections.Immutable;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Rules;

/// <summary>
/// Classifies the whole surface of every type the built-in rules target against the reference pack this project
/// compiles against. A type is targeted when a rule names or substitutes it, or when one of its members' signatures
/// mentions a process-control type: a public class, struct, or interface of the assembly that declares
/// <see cref="System.Diagnostics.Process"/>. Members a delegate declares invoke its target and are not scanned.
/// Every public and protected method, constructor, accessor, operator, and field of a targeted type must be controlled,
/// rejected, substituted, or rebuilt by the rules, or listed in <see cref="UnrewrittenMembers"/> with the reason it stays
/// a BCL call; a listed member that the pack lacks or that a rule matches is stale. A runtime that adds a member to a
/// targeted type, or a process-control type to its process assembly, therefore fails these tests until the member is
/// classified, and a runtime that adds an assembly fails the snapshot of the pack's assemblies.
/// </summary>
[Collection(ReferencePackTests.Name)]
public sealed class BuiltInRuleSurfaceTests
{
    private const string SnapshotVariable = "CLOCKWORK_UPDATE_SNAPSHOTS";

    private static readonly string[] KnownProcessControlTypes =
    [
        "Microsoft.Win32.SafeHandles.SafeProcessHandle",
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
        "System.Runtime.InteropServices.UnixProcessStartArguments",
        "System.Runtime.InteropServices.WindowsProcessStartArguments",
    ];

    private static readonly Lazy<Surface> Targets = new(Surface.Find);

    public static TheoryData<string> TargetedTypes => [.. Targets.Value.TargetedTypes.Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(TargetedTypes))]
    public void EveryMemberOfATargetedTypeIsClassified(string typeFullName)
    {
        Assert.True(ReferencePack.Shared.Types.TryGetValue(typeFullName, out TypeDefinition? type), $"'{typeFullName}' is not a public type of the reference pack.");
        Dictionary<string, UnrewrittenMember> listed = UnrewrittenMembers.All
            .Where(member => member.DeclaringTypeFullName == typeFullName)
            .ToDictionary(member => member.Member, StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var unclassified = new List<string>();
        var stale = new List<string>();
        foreach (IMemberDefinition member in ReferencePack.VisibleMembers(type))
        {
            string key = ReferencePack.MemberKey(member);
            Assert.True(members.Add(key), $"Two members of '{typeFullName}' are both named '{key}'.");
            RuleClassification classification = BuiltInRuleClassifier.Shared.Classify(member);
            bool isListed = listed.ContainsKey(key);
            if (classification.Outcome != RuleOutcome.None && isListed)
            {
                stale.Add($"    \"{key}\" ({classification})");
            }
            else if (classification.Outcome == RuleOutcome.None && !isListed)
            {
                unclassified.Add($"    \"{key}\",");
            }
        }

        stale.AddRange(listed.Keys.Where(key => !members.Contains(key)).Select(key => $"    \"{key}\" (not a public or protected member)"));
        Assert.True(
            unclassified.Count == 0 && stale.Count == 0,
            $"'{typeFullName}' has members the built-in rules do not classify. Give each unclassified member a rule, or list it " +
            $"in {nameof(UnrewrittenMembers)} with the reason it stays a BCL call, and remove each stale entry." + Environment.NewLine +
            "Unclassified:" + Environment.NewLine + string.Join(Environment.NewLine, unclassified) + Environment.NewLine +
            "Stale:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void EveryListedMemberBelongsToATargetedTypeOnce()
    {
        ImmutableHashSet<string> targeted = Targets.Value.TargetedTypes;
        string[] duplicates =
        [
            .. UnrewrittenMembers.All
                .GroupBy(member => member.DeclaringTypeFullName + "::" + member.Member, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];
        string[] untargeted =
        [
            .. UnrewrittenMembers.All
                .Select(member => member.DeclaringTypeFullName)
                .Where(type => !targeted.Contains(type))
                .Distinct(StringComparer.Ordinal),
        ];

        Assert.True(duplicates.Length == 0, "Members listed more than once:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
        Assert.True(untargeted.Length == 0, "Listed members of types no rule targets:" + Environment.NewLine + string.Join(Environment.NewLine, untargeted));
    }

    [Fact]
    public void EveryMemberWithAProcessControlSignatureIsRejectedOrListed()
    {
        Surface surface = Targets.Value;
        HashSet<string> listed = [.. UnrewrittenMembers.All.Select(member => member.DeclaringTypeFullName + "::" + member.Member)];
        var violations = new List<string>();
        foreach (IMemberDefinition member in surface.ProcessControlMembers)
        {
            string name = member.DeclaringType.FullName + "::" + ReferencePack.MemberKey(member);
            RuleClassification classification = BuiltInRuleClassifier.Shared.Classify(member);
            if (classification.Outcome != RuleOutcome.Rejected && !listed.Contains(name))
            {
                violations.Add($"{name} ({classification})");
            }
        }

        Assert.Superset(KnownProcessControlTypes.ToHashSet(StringComparer.Ordinal), surface.ProcessControlTypes);
        Assert.Contains(
            surface.ProcessControlMembers,
            member => member.DeclaringType.FullName == "System.Diagnostics.Process" && member.Name == "Start");
        Assert.True(
            violations.Count == 0,
            "Members whose signatures mention a process-control type must be rejected or listed:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ReferencePackAssembliesMatchTheSnapshot()
    {
        string[] actual = [.. ReferencePack.Shared.AssemblyFileNames];
        string path = Repository.PathOf("tests", "Clockwork.Instrumentation.Tests", "Rules", "ReferencePackAssemblies.txt");
        if (Environment.GetEnvironmentVariable(SnapshotVariable) == "1")
        {
            File.WriteAllText(path, string.Join('\n', actual) + "\n");
        }

        string[] expected = File.Exists(path) ? [.. File.ReadAllLines(path).Where(line => line.Length > 0)] : [];
        string[] added = [.. actual.Except(expected, StringComparer.Ordinal)];
        string[] removed = [.. expected.Except(actual, StringComparer.Ordinal)];
        Assert.True(
            added.Length == 0 && removed.Length == 0,
            $"The reference pack at '{ReferencePack.Shared.Directory}' no longer matches '{path}'." + Environment.NewLine +
            "Added: " + string.Join(", ", added) + Environment.NewLine +
            "Removed: " + string.Join(", ", removed) + Environment.NewLine +
            "Review each added assembly for members that read a clock, entropy, or host state, schedule or block work, or start " +
            "or control a process, and give them rules (BuiltInRuleSets) or list them with a reason (UnrewrittenMembers) when " +
            $"they belong to a targeted type. Then refresh the snapshot with `$env:{SnapshotVariable}='1'; dotnet run --project " +
            "tests/Clockwork.Instrumentation.Tests/Clockwork.Instrumentation.Tests.csproj -- --filter-class " +
            $"{typeof(BuiltInRuleSurfaceTests).FullName}`.");
    }

    // The targeted types and the process-control signatures, found by one scan of the reference pack.
    private sealed record Surface(
        ImmutableHashSet<string> TargetedTypes,
        ImmutableHashSet<string> ProcessControlTypes,
        ImmutableArray<IMemberDefinition> ProcessControlMembers)
    {
        public static Surface Find()
        {
            ReferencePack pack = ReferencePack.Shared;
            ModuleDefinition processModule = pack.Types["System.Diagnostics.Process"].Module;
            ImmutableHashSet<string> processControlTypes =
            [
                .. processModule.GetTypes()
                    .Where(type => ReferencePack.IsVisible(type) && !type.IsEnum && !IsDelegate(type))
                    .Select(type => type.FullName),
            ];

            // A delegate's members invoke its target, which is classified where it is declared.
            ImmutableArray<IMemberDefinition> processControlMembers =
            [
                .. pack.Types.Values
                    .Where(type => !IsDelegate(type))
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .SelectMany(ReferencePack.VisibleMembers)
                    .Where(member => SignatureMentions.Find((MemberReference)member, processControlTypes).Count > 0),
            ];
            ImmutableHashSet<string> targetedTypes =
            [
                .. BuiltInRuleClassifier.Shared.Rules.Select(rule => rule.Target.DeclaringTypeFullName),
                .. processControlMembers.Select(member => member.DeclaringType.FullName),
            ];
            return new Surface(targetedTypes, processControlTypes, processControlMembers);
        }

        private static bool IsDelegate(TypeDefinition type) => type.BaseType?.FullName == "System.MulticastDelegate";
    }
}
