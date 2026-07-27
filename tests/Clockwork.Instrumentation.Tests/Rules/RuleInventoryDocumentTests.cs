using System.Runtime.CompilerServices;
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
    }

    private static string InventoryPath()
    {
        string dir = Path.GetDirectoryName(ThisFile())!;
        // tests/Clockwork.Instrumentation.Tests/Rules -> repo root is three levels up.
        string repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        return Path.Combine(repoRoot, "docs", "rule-inventory.md");
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
