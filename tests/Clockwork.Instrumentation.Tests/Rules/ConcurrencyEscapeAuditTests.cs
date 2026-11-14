using System.Collections.Immutable;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Rules;

/// <summary>
/// Classifies every framework member which reaches a concurrency escape, as <see cref="ConcurrencyEscapeScanner"/> finds
/// them in the implementation assemblies of the runtime the tests run on. Only members of the reference pack count,
/// because those are what an application compiles against. Each must be controlled or rejected by a built-in rule,
/// listed in <see cref="UnrewrittenMembers"/>, or listed in <see cref="ConcurrencyEscapeAudit"/>, by member or by its
/// whole assembly, with the reason it needs no rule. The implementation assemblies differ by operating system, so the list covers every platform's candidates.
/// A runtime that adds a candidate fails these tests until it is classified, and a listed member that the reference pack
/// no longer has, or that a rule now classifies, is stale. To check another operating system's implementation assemblies,
/// set <c>CLOCKWORK_ESCAPE_FRAMEWORK_DIRECTORY</c> to the <c>lib</c> directory of its extracted
/// <c>Microsoft.NETCore.App.Runtime.&lt;rid&gt;</c> package.
/// </summary>
[Collection(ReferencePackGroup.Name)]
public sealed class ConcurrencyEscapeAuditTests
{
    private static readonly Lazy<Audit> Result = new(Audit.Run);

    [Fact]
    public void EveryCandidateIsClassified()
    {
        Audit audit = Result.Value;
        Dictionary<string, AuditedEscape> listed = ConcurrencyEscapeAudit.All.ToDictionary(member => member.QualifiedKey, StringComparer.Ordinal);
        HashSet<string> unrewritten = [.. UnrewrittenMembers.All.Select(member => member.QualifiedKey)];
        Dictionary<string, AuditedAssembly> assemblies = ConcurrencyEscapeAudit.Assemblies.ToDictionary(assembly => assembly.Name, StringComparer.Ordinal);
        var unclassified = new List<string>();
        var stale = new List<string>();
        foreach ((EscapeCandidate candidate, RuleClassification classification) in audit.Candidates)
        {
            bool byRule = classification.Outcome is not (RuleOutcome.None or RuleOutcome.NewObjOnly);
            bool byUnrewritten = unrewritten.Contains(candidate.QualifiedKey);
            bool byAssembly = assemblies.TryGetValue(candidate.Assembly, out AuditedAssembly assembly);
            if (listed.TryGetValue(candidate.QualifiedKey, out AuditedEscape entry))
            {
                if (byRule || byUnrewritten)
                {
                    stale.Add($"    {candidate.QualifiedKey} ({(byRule ? classification.ToString() : $"listed in {nameof(UnrewrittenMembers)}")})");
                }
                else if (byAssembly && assembly.Reason == entry.Reason)
                {
                    stale.Add($"    {candidate.QualifiedKey} (its assembly, {candidate.Assembly}, is classified {entry.Reason})");
                }
            }
            else if (!byRule && !byUnrewritten && !byAssembly)
            {
                unclassified.Add(
                    $"    {candidate.QualifiedKey} ({candidate.Assembly}, {candidate.Sink} at depth {candidate.Depth}: " +
                    string.Join(" -> ", candidate.Path) + ")");
            }
        }

        HashSet<string> found = [.. audit.Candidates.Select(entry => entry.Candidate.QualifiedKey)];
        // The implementation assemblies differ by operating system, so the list is the union of every platform's
        // candidates: an entry this platform's scan does not find is stale only when the reference pack lacks it.
        HashSet<string> referenceMembers = [.. ReferencePack.Shared.Types.Values.SelectMany(ReferencePack.VisibleMembers).Select(ReferencePack.QualifiedKey)];
        stale.AddRange(listed.Keys
            .Where(key => !found.Contains(key) && !referenceMembers.Contains(key))
            .Order(StringComparer.Ordinal)
            .Select(key => $"    {key} (not a member of the reference pack)"));

        // The shared framework ships the same managed assemblies on every operating system.
        stale.AddRange(assemblies.Keys
            .Except(ConcurrencyEscapeScanner.AssemblyNames, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => $"    assembly {name} (not in the shared framework)"));
        Assert.True(
            unclassified.Count == 0 && stale.Count == 0,
            $"The concurrency-escape scan of '{ConcurrencyEscapeScanner.FrameworkDirectory}' found members the built-in rules " +
            $"do not classify. Give each unclassified member a rule, or list it in {nameof(ConcurrencyEscapeAudit)} with the " +
            "reason it needs no rule (or classify its whole assembly with one reason), and remove or correct each stale entry." + Environment.NewLine +
            "Unclassified:" + Environment.NewLine + string.Join(Environment.NewLine, unclassified) + Environment.NewLine +
            "Stale:" + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void EveryFamilyThatClassifiesACandidateIsInTheStatusTable()
    {
        Dictionary<string, BuiltInRuleFamily> families = BuiltInRuleSets.DeterministicBclInventory
            .Concat(BuiltInRuleSets.ControlledTasksInventory)
            .ToDictionary(entry => entry.Rule.Id, entry => entry.Family, StringComparer.Ordinal);
        BuiltInRuleFamily[] missing =
        [
            .. Result.Value.Candidates
                .Select(entry => entry.Classification.RuleId)
                .OfType<string>()
                .Select(id => families[id])
                .Distinct()
                .Except(RuleInventoryDocument.ConcurrencyFamilies)
                .Order(),
        ];

        Assert.True(
            missing.Length == 0,
            $"Families whose rules classify a concurrency escape are missing from {nameof(RuleInventoryDocument)}." +
            $"{nameof(RuleInventoryDocument.ConcurrencyFamilies)}: {string.Join(", ", missing)}.");
    }

    // An oracle for the scanner itself: one member of each escape kind it must find, so a scan that silently finds less
    // fails here rather than passing every classification check.
    [Theory]
    [InlineData("System.Threading.Tasks.Task::Run(System.Action)", nameof(EscapeSink.TaskScheduler))]
    [InlineData("System.Threading.ThreadPool::QueueUserWorkItem(System.Threading.WaitCallback)", nameof(EscapeSink.ThreadPool))]
    [InlineData("System.Threading.Thread::Start()", nameof(EscapeSink.Thread))]
    [InlineData("System.Threading.Timer::.ctor(System.Threading.TimerCallback)", nameof(EscapeSink.Timer))]
    [InlineData("System.Threading.Channels.ChannelReader`1::ReadAllAsync(System.Threading.CancellationToken)", nameof(EscapeSink.AsyncMethod))]
    [InlineData("System.Net.Http.HttpClient::GetStringAsync(System.String)", nameof(EscapeSink.AsyncMethod))]
    [InlineData("System.ComponentModel.BackgroundWorker::RunWorkerAsync()", nameof(EscapeSink.TaskScheduler))]
    [InlineData("System.Threading.Tasks.Dataflow.ActionBlock`1::.ctor(System.Action`1<!0>)", nameof(EscapeSink.TaskScheduler))]
    [InlineData("System.Net.WebSockets.WebSocket::CreateFromStream(System.IO.Stream,System.Net.WebSockets.WebSocketCreationOptions)", nameof(EscapeSink.Timer))]
    public void ScanFindsEveryKindOfEscape(string qualifiedKey, string sink)
    {
        (EscapeCandidate candidate, _) = Assert.Single(Result.Value.Candidates, entry => entry.Candidate.QualifiedKey == qualifiedKey);
        Assert.Equal(sink, candidate.Sink.ToString());
    }

    // The scan's candidates that are members of the reference pack, each with its classification by the built-in rules.
    private sealed record Audit(ImmutableArray<(EscapeCandidate Candidate, RuleClassification Classification)> Candidates)
    {
        public static Audit Run()
        {
            ImmutableArray<EscapeCandidate> scanned = ConcurrencyEscapeScanner.ScanAll();
            var candidates = ImmutableArray.CreateBuilder<(EscapeCandidate, RuleClassification)>();
            foreach (EscapeCandidate candidate in scanned)
            {
                if (ReferenceMember(candidate) is IMemberDefinition member)
                {
                    candidates.Add((candidate, BuiltInRuleClassifier.Shared.Classify(member)));
                }
            }

            return new Audit(candidates.ToImmutable());
        }

        private static IMemberDefinition? ReferenceMember(EscapeCandidate candidate)
        {
            if (!ReferencePack.Shared.Types.TryGetValue(candidate.Member.DeclaringType.FullName, out TypeDefinition? type))
            {
                return null;
            }

            string key = ReferencePack.MemberKey(candidate.Member);
            return ReferencePack.VisibleMembers(type).FirstOrDefault(member => ReferencePack.MemberKey(member) == key);
        }
    }
}
