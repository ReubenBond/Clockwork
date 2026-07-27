using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;

namespace Clockwork.Instrumentation.Tests.Rules;

/// <summary>
/// Guards the published deterministic BCL rule inventory (<c>docs/rule-inventory.md</c>) against drift:
/// the committed file must match <see cref="RuleInventoryDocument.Render()"/> byte-for-byte. Set the
/// <c>CLOCKWORK_UPDATE_DOCS=1</c> environment variable to regenerate the file instead of asserting.
/// </summary>
public sealed class RuleInventoryDocumentTests
{
    [Fact]
    public void CommittedInventoryMatchesGeneratedContent()
    {
        string expected = RuleInventoryDocument.Render();
        string path = InventoryPath();

        if (Environment.GetEnvironmentVariable("CLOCKWORK_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        string actual = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Equal(expected, actual);
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
            .Where(entry => entry.Family == BuiltInRuleFamily.TaskDeferred)
            .Select(entry => string.Join(",", entry.Rule.Target.ParameterTypeFullNames))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(frameworkOverloads, classifiedOverloads);
        Assert.All(
            BuiltInRuleSets.ControlledTasksInventory.Where(entry => entry.Family == BuiltInRuleFamily.TaskDeferred),
            entry => Assert.Equal(Clockwork.Runtime.Policy.SimulationApiPolicy.Rejected, entry.Rule.Policy));
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

    private static string MethodShape(MethodInfo method) =>
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
