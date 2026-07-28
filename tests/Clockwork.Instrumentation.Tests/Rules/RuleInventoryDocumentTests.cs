using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;

namespace Clockwork.Instrumentation.Tests.Rules;

/// <summary>
/// Guards the generated rule inventory and records the repository path and command used by the separate
/// documentation-refresh task.
/// </summary>
public sealed class RuleInventoryDocumentTests
{
    [Fact]
    public void GeneratedInventoryExposesRefreshCommandAndPath()
    {
        string rendered = RuleInventoryDocument.Render();
        string path = InventoryPath();

        if (Environment.GetEnvironmentVariable("CLOCKWORK_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, rendered);
        }

        Assert.NotEmpty(rendered);
        Assert.True(
            File.Exists(path),
            $"Refresh '{path}' with `$env:CLOCKWORK_UPDATE_DOCS='1'; dotnet test tests\\Clockwork.Instrumentation.Tests\\Clockwork.Instrumentation.Tests.csproj --filter-class Clockwork.Instrumentation.Tests.Rules.RuleInventoryDocumentTests`.");
    }

    [Fact]
    public void InventoryCoversEveryShippedRule()
    {
        string rendered = RuleInventoryDocument.Render();
        foreach ((_, RewriteRule rule) in BuiltInRuleSets.DeterministicBclInventory)
        {
            Assert.Contains(rule.Id, rendered, StringComparison.Ordinal);
        }

        foreach ((_, RewriteRule rule) in BuiltInRuleSets.ControlledTasksInventory)
        {
            Assert.Contains(rule.Id, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TaskDelayRulesClassifyEveryNet10Overload()
    {
        string[] frameworkOverloads = typeof(Task)
            .GetMethods()
            .Where(method => method.IsStatic && method.Name == nameof(Task.Delay))
            .Select(method => string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] classifiedOverloads = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == BuiltInRuleFamily.TaskTime && entry.Rule.Target.MemberName == "Delay")
            .Select(entry => string.Join(",", entry.Rule.Target.ParameterTypeFullNames))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(frameworkOverloads, classifiedOverloads);
        Assert.All(
            BuiltInRuleSets.ControlledTasksInventory.Where(
                entry => entry.Family == BuiltInRuleFamily.TaskTime && entry.Rule.Target.MemberName == "Delay"),
            entry => Assert.Equal(Clockwork.Runtime.Policy.SimulationApiPolicy.Controlled, entry.Rule.Policy));
    }

    [Fact]
    public void TaskWaitAsyncRulesClassifyEveryNet10Overload()
    {
        int frameworkOverloads =
            typeof(Task).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(method => method.Name == nameof(Task.WaitAsync)) +
            typeof(Task<>).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Count(method => method.Name == nameof(Task.WaitAsync));
        RewriteRule[] classified = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == BuiltInRuleFamily.TaskTime && entry.Rule.Target.MemberName == "WaitAsync")
            .Select(entry => entry.Rule)
            .ToArray();

        Assert.Equal(10, frameworkOverloads);
        Assert.Equal(frameworkOverloads, classified.Length);
        Assert.All(classified, rule =>
            Assert.Equal(Clockwork.Runtime.Policy.SimulationApiPolicy.Controlled, rule.Policy));
    }

    [Fact]
    public void TimerTypesUseControlledWholeTypeSubstitutions()
    {
        (string Target, string Replacement)[] expected =
        [
            ("System.Threading.Timer", "Clockwork.Runtime.Threading.ControlledTimer"),
            ("System.Timers.Timer", "Clockwork.Runtime.Threading.ControlledTimersTimer"),
            ("System.Threading.PeriodicTimer", "Clockwork.Runtime.Threading.ControlledPeriodicTimer"),
        ];

        foreach ((string target, string replacement) in expected)
        {
            RewriteRule rule = Assert.Single(BuiltInRuleSets.ControlledTasksInventory
                .Where(entry => entry.Family == BuiltInRuleFamily.Timers)
                .Select(entry => entry.Rule),
                rule => rule.Target.DeclaringTypeFullName == target);
            Assert.Equal(RewriteOperationKind.SubstituteType, rule.Operation);
            Assert.Equal(replacement, rule.Replacement.DeclaringTypeFullName);
        }
    }

    [Fact]
    public void TaskFactoryRulesClassifyEveryNet10StartNewOverload()
    {
        int frameworkOverloadCount =
            typeof(TaskFactory).GetMethods().Count(method => method.Name == nameof(TaskFactory.StartNew)) +
            typeof(TaskFactory<>).GetMethods().Count(method => method.Name == nameof(TaskFactory.StartNew));
        RewriteRule[] classified = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == BuiltInRuleFamily.TaskFactory)
            .Select(entry => entry.Rule)
            .ToArray();

        Assert.Equal(24, frameworkOverloadCount);
        Assert.Equal(frameworkOverloadCount, classified.Length);
        Assert.All(classified, rule =>
            Assert.Equal(Clockwork.Runtime.Policy.SimulationApiPolicy.Controlled, rule.Policy));
    }

    [Fact]
    public void DocumentedHolesDoNotNameFullyClassifiedFamilies()
    {
        string rendered = RuleInventoryDocument.Render();
        string holes = rendered[rendered.IndexOf("## Documented holes", StringComparison.Ordinal)..];

        Assert.DoesNotMatch(new Regex(@"\bTask\.Delay\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bTaskFactory\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bMonitor\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"(?<!ReaderWriter)\bLock\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bSemaphoreSlim\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bReaderWriterLockSlim\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bManualResetEventSlim\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bSpinLock\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bBarrier\b", RegexOptions.CultureInvariant), holes);
        Assert.DoesNotMatch(new Regex(@"\bCountdownEvent\b", RegexOptions.CultureInvariant), holes);
    }

    [Fact]
    public void MonitorRulesClassifyEveryNet10Method()
    {
        string[] framework = typeof(Monitor)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(MethodShape)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] classified = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == BuiltInRuleFamily.Monitor)
            .Select(entry => entry.Rule.Target.MemberName + "(" +
                string.Join(",", entry.Rule.Target.ParameterTypeFullNames) + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(framework, classified);
    }

    [Fact]
    public void SemaphoreSlimRulesClassifyEveryNet10DeclaredMember()
    {
        int frameworkMemberCount =
            typeof(SemaphoreSlim).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length +
            typeof(SemaphoreSlim).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Length;
        RewriteRule[] classified = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == BuiltInRuleFamily.Semaphore)
            .Select(entry => entry.Rule)
            .ToArray();

        Assert.Equal(19, frameworkMemberCount);
        Assert.Equal(frameworkMemberCount, classified.Length);
        Assert.Equal(classified.Length, classified.Select(rule => rule.Target.ToCanonicalString()).Distinct().Count());
    }

    [Fact]
    public void ModernSynchronizationReceiverFirstRulesClassifyEveryNet10DeclaredMember()
    {
        AssertFamilyMatchesPublicMembers(typeof(ReaderWriterLockSlim), BuiltInRuleFamily.ReaderWriterLockSlim, includeConstructors: true);
        AssertFamilyMatchesPublicMembers(typeof(ManualResetEventSlim), BuiltInRuleFamily.ManualResetEventSlim, includeConstructors: true);
        AssertFamilyMatchesPublicMembers(typeof(Mutex), BuiltInRuleFamily.Mutex, includeConstructors: true);
        AssertFamilyMatchesPublicMembers(typeof(Semaphore), BuiltInRuleFamily.KernelSemaphore, includeConstructors: true);
        AssertFamilyMatchesPublicMembers(typeof(ExecutionContext), BuiltInRuleFamily.ExecutionContext, includeConstructors: false);
        AssertFamilyMatchesPublicMembers(typeof(SynchronizationContext), BuiltInRuleFamily.SynchronizationContext, includeConstructors: false);
    }

    [Fact]
    public void ModernSynchronizationWholeTypeSubstitutionsUseControlledRuntimeTypes()
    {
        (string Target, string Replacement)[] expected =
        [
            ("System.Threading.SpinLock", "Clockwork.Runtime.Threading.ControlledSpinLock"),
            ("System.Threading.Barrier", "Clockwork.Runtime.Threading.ControlledBarrier"),
            ("System.Threading.CountdownEvent", "Clockwork.Runtime.Threading.ControlledCountdownEvent"),
        ];

        foreach ((string target, string replacement) in expected)
        {
            RewriteRule rule = Assert.Single(BuiltInRuleSets.ControlledTasksInventory
                .Where(entry => entry.Rule.Target.DeclaringTypeFullName == target)
                .Select(entry => entry.Rule));
            Assert.Equal(RewriteOperationKind.SubstituteType, rule.Operation);
            Assert.Equal(replacement, rule.Replacement.DeclaringTypeFullName);
        }
    }

    private static void AssertFamilyMatchesPublicMembers(Type type, BuiltInRuleFamily family, bool includeConstructors)
    {
        IEnumerable<MethodBase> framework = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (includeConstructors)
        {
            framework = framework.Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        string[] expected = framework.Select(MethodShape).Order(StringComparer.Ordinal).ToArray();
        string[] actual = BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => entry.Family == family)
            .Select(entry => entry.Rule.Target.MemberName + "(" +
                string.Join(",", entry.Rule.Target.ParameterTypeFullNames) + ")")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static string MethodShape(MethodBase method) =>
        method.Name + "(" + string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName)) + ")";

    private static string InventoryPath()
    {
        string dir = Path.GetDirectoryName(ThisFile())!;
        // tests/Clockwork.Instrumentation.Tests/Rules -> repo root is three levels up.
        string repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        return Path.Combine(repoRoot, "docs", "rule-inventory.md");
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
