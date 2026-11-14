using System.Collections.Immutable;

namespace Clockwork.Instrumentation.Rules.BuiltIn;

/// <summary>A framework member which reaches a concurrency escape and needs no rule, with the reason.</summary>
/// <param name="DeclaringTypeFullName">The Cecil full name of the declaring type.</param>
/// <param name="MemberKey">The member as a call site names it, in the form <see cref="UnrewrittenMember.MemberKey"/> uses.</param>
/// <param name="Reason">Why the member needs no rule.</param>
internal readonly record struct AuditedEscape(string DeclaringTypeFullName, string MemberKey, EscapeReason Reason)
{
    /// <summary>Gets the member qualified by its declaring type: <c>DeclaringType::MemberKey</c>.</summary>
    public string QualifiedKey => DeclaringTypeFullName + "::" + MemberKey;
}

/// <summary>
/// The classified result of the concurrency-escape audit. A test scans the implementation assemblies of the shared
/// framework the tests run on, those in <see cref="Assemblies"/>, for the public members (and protected members of
/// unsealed types) of the reference pack that reach thread-pool queueing, <c>TaskScheduler.Default</c> or the members
/// which schedule onto it (<c>Task.Run</c>, <c>Task.Start</c>, <c>TaskFactory.StartNew</c>), <c>Thread.Start</c>, a real
/// timer, or an async method, within <see cref="MaxDepth"/> calls inside one assembly. Every such member must be
/// controlled or rejected by a built-in rule, listed in <see cref="UnrewrittenMembers"/>, or listed here with the reason
/// it needs no rule; the test fails on a member a new runtime adds, and on a listed member the scan no longer finds or a
/// rule now classifies. <see cref="RuleInventoryDocument"/> publishes the result as the concurrency status table.
/// </summary>
internal static class ConcurrencyEscapeAudit
{
    /// <summary>
    /// The longest chain of calls from a member to an escape that the scan follows. A call to another overload of the
    /// caller, or to a local function or lambda compiled from its body, does not count. Four calls reach the escapes
    /// behind the framework's layered stream and socket overloads; a fifth starts reaching the BCL's blocking waits,
    /// which only read the current scheduler.
    /// </summary>
    public const int MaxDepth = 4;

    /// <summary>
    /// Gets the simple names of the scanned assemblies, each a home of BCL concurrency an application calls directly:
    /// CoreLib (tasks, the thread pool, threads, timers, and the stream and text I/O base types), the concurrency
    /// libraries (concurrent collections, pipelines, async LINQ, PLINQ, channels, and <c>Parallel</c>), and the
    /// networking stacks whose operations complete on threads of the BCL's choosing (sockets and HTTP). The other
    /// shared-framework assemblies wrap these, so their concurrency reaches the same CoreLib members, or perform OS I/O
    /// which a simulation substitutes as a whole. An assembly the runtime does not ship is not scanned.
    /// </summary>
    public static ImmutableArray<string> Assemblies { get; } =
    [
        "System.Private.CoreLib",
        "System.Collections.Concurrent",
        "System.IO.Pipelines",
        "System.Linq.AsyncEnumerable",
        "System.Linq.Parallel",
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Threading.Channels",
        "System.Threading.Tasks.Parallel",
    ];

    /// <summary>Gets every listed member, grouped by declaring type and then by reason.</summary>
    public static ImmutableArray<AuditedEscape> All { get; } = Build();

    private static ImmutableArray<AuditedEscape> Build()
    {
        var members = ImmutableArray.CreateBuilder<AuditedEscape>();
        void Add(string type, EscapeReason reason, params string[] memberKeys)
        {
            foreach (string memberKey in memberKeys)
            {
                members.Add(new AuditedEscape(type, memberKey, reason));
            }
        }

        Add("System.CodeDom.Compiler.IndentedTextWriter", EscapeReason.FrameworkAwait,
            "FlushAsync()",
            "FlushAsync(System.Threading.CancellationToken)",
            "OutputTabsAsync()",
            "WriteAsync(System.Char)",
            "WriteAsync(System.Char[],System.Int32,System.Int32)",
            "WriteAsync(System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "WriteAsync(System.String)",
            "WriteAsync(System.Text.StringBuilder,System.Threading.CancellationToken)",
            "WriteLineAsync()",
            "WriteLineAsync(System.Char)",
            "WriteLineAsync(System.Char[],System.Int32,System.Int32)",
            "WriteLineAsync(System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "WriteLineAsync(System.String)",
            "WriteLineAsync(System.Text.StringBuilder,System.Threading.CancellationToken)",
            "WriteLineNoTabsAsync(System.String)");

        Add("System.Diagnostics.Tracing.EventListener", EscapeReason.AffinityBackstop,
            "DisableEvents(System.Diagnostics.Tracing.EventSource)",
            "Dispose()",
            "EnableEvents(System.Diagnostics.Tracing.EventSource,System.Diagnostics.Tracing.EventLevel)",
            "EnableEvents(System.Diagnostics.Tracing.EventSource,System.Diagnostics.Tracing.EventLevel,System.Diagnostics.Tracing.EventKeywords)",
            "EnableEvents(System.Diagnostics.Tracing.EventSource,System.Diagnostics.Tracing.EventLevel,System.Diagnostics.Tracing.EventKeywords,System.Collections.Generic.IDictionary`2<System.String,System.String>)");

        Add("System.IO.BinaryWriter", EscapeReason.FrameworkAwait,
            "DisposeAsync()");

        Add("System.IO.BufferedStream", EscapeReason.FrameworkAwait,
            "BeginRead(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)",
            "BeginWrite(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)",
            "CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)",
            "DisposeAsync()",
            "FlushAsync(System.Threading.CancellationToken)",
            "ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)",
            "ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)",
            "WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)",
            "WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)");

        Add("System.IO.File", EscapeReason.OsResource,
            "AppendAllBytesAsync(System.String,System.Byte[],System.Threading.CancellationToken)",
            "AppendAllBytesAsync(System.String,System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)",
            "AppendAllLinesAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Text.Encoding,System.Threading.CancellationToken)",
            "AppendAllLinesAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Threading.CancellationToken)",
            "AppendAllTextAsync(System.String,System.ReadOnlyMemory`1<System.Char>,System.Text.Encoding,System.Threading.CancellationToken)",
            "AppendAllTextAsync(System.String,System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "AppendAllTextAsync(System.String,System.String,System.Text.Encoding,System.Threading.CancellationToken)",
            "AppendAllTextAsync(System.String,System.String,System.Threading.CancellationToken)",
            "ReadAllBytesAsync(System.String,System.Threading.CancellationToken)",
            "ReadAllLinesAsync(System.String,System.Text.Encoding,System.Threading.CancellationToken)",
            "ReadAllLinesAsync(System.String,System.Threading.CancellationToken)",
            "ReadAllTextAsync(System.String,System.Text.Encoding,System.Threading.CancellationToken)",
            "ReadAllTextAsync(System.String,System.Threading.CancellationToken)",
            "ReadLinesAsync(System.String,System.Text.Encoding,System.Threading.CancellationToken)",
            "ReadLinesAsync(System.String,System.Threading.CancellationToken)",
            "WriteAllBytesAsync(System.String,System.Byte[],System.Threading.CancellationToken)",
            "WriteAllBytesAsync(System.String,System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)",
            "WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Text.Encoding,System.Threading.CancellationToken)",
            "WriteAllLinesAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Threading.CancellationToken)",
            "WriteAllTextAsync(System.String,System.ReadOnlyMemory`1<System.Char>,System.Text.Encoding,System.Threading.CancellationToken)",
            "WriteAllTextAsync(System.String,System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "WriteAllTextAsync(System.String,System.String,System.Text.Encoding,System.Threading.CancellationToken)",
            "WriteAllTextAsync(System.String,System.String,System.Threading.CancellationToken)");

        Add("System.IO.FileStream", EscapeReason.OsResource,
            "BeginRead(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)",
            "BeginWrite(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)",
            "CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)",
            "DisposeAsync()",
            "FlushAsync(System.Threading.CancellationToken)",
            "ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)",
            "ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)",
            "WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)",
            "WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)");

        Add("System.IO.MemoryStream", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)");

        Add("System.IO.Pipelines.PipeReader", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Pipelines.PipeWriter,System.Threading.CancellationToken)",
            "CopyToAsync(System.IO.Stream,System.Threading.CancellationToken)",
            "ReadAtLeastAsync(System.Int32,System.Threading.CancellationToken)",
            "ReadAtLeastAsyncCore(System.Int32,System.Threading.CancellationToken)");

        Add("System.IO.Pipelines.PipeWriter", EscapeReason.FrameworkAwait,
            "CopyFromAsync(System.IO.Stream,System.Threading.CancellationToken)");

        Add("System.IO.Pipelines.StreamPipeExtensions", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Stream,System.IO.Pipelines.PipeWriter,System.Threading.CancellationToken)");

        Add("System.IO.RandomAccess", EscapeReason.OsResource,
            "ReadAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Collections.Generic.IReadOnlyList`1<System.Memory`1<System.Byte>>,System.Int64,System.Threading.CancellationToken)",
            "ReadAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Memory`1<System.Byte>,System.Int64,System.Threading.CancellationToken)",
            "WriteAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.Collections.Generic.IReadOnlyList`1<System.ReadOnlyMemory`1<System.Byte>>,System.Int64,System.Threading.CancellationToken)",
            "WriteAsync(Microsoft.Win32.SafeHandles.SafeFileHandle,System.ReadOnlyMemory`1<System.Byte>,System.Int64,System.Threading.CancellationToken)");

        Add("System.IO.ReadOnlyMemoryStream", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)");


        Add("System.IO.StreamReader", EscapeReason.FrameworkAwait,
            "ReadAsync(System.Char[],System.Int32,System.Int32)",
            "ReadAsync(System.Memory`1<System.Char>,System.Threading.CancellationToken)",
            "ReadBlockAsync(System.Char[],System.Int32,System.Int32)",
            "ReadBlockAsync(System.Memory`1<System.Char>,System.Threading.CancellationToken)",
            "ReadLineAsync()",
            "ReadLineAsync(System.Threading.CancellationToken)",
            "ReadToEndAsync()",
            "ReadToEndAsync(System.Threading.CancellationToken)");

        Add("System.IO.StreamWriter", EscapeReason.FrameworkAwait,
            "DisposeAsync()",
            "FlushAsync()",
            "FlushAsync(System.Threading.CancellationToken)",
            "WriteAsync(System.Char)",
            "WriteAsync(System.Char[],System.Int32,System.Int32)",
            "WriteAsync(System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "WriteAsync(System.String)",
            "WriteLineAsync()",
            "WriteLineAsync(System.Char)",
            "WriteLineAsync(System.Char[],System.Int32,System.Int32)",
            "WriteLineAsync(System.ReadOnlyMemory`1<System.Char>,System.Threading.CancellationToken)",
            "WriteLineAsync(System.String)");

        Add("System.IO.StringWriter", EscapeReason.CompletesOnCaller,
            "WriteAsync(System.Text.StringBuilder,System.Threading.CancellationToken)",
            "WriteLineAsync(System.Text.StringBuilder,System.Threading.CancellationToken)");



        Add("System.IO.WritableMemoryStream", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)");

        Add("System.Linq.AsyncEnumerable", EscapeReason.FrameworkAwait,
            "AggregateAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,!!0,!!0>,System.Threading.CancellationToken)",
            "AggregateAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!0>>,System.Threading.CancellationToken)",
            "AggregateAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!1,System.Func`3<!!1,!!0,!!1>,System.Threading.CancellationToken)",
            "AggregateAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!1,System.Func`4<!!1,!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Threading.CancellationToken)",
            "AggregateAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!1,System.Func`3<!!1,!!0,!!1>,System.Func`2<!!1,!!2>,System.Threading.CancellationToken)",
            "AggregateAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!1,System.Func`4<!!1,!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Threading.CancellationToken)",
            "AggregateBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,!!2,System.Func`3<!!2,!!0,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "AggregateBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`2<!!1,!!2>,System.Func`3<!!2,!!0,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "AggregateBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,!!2,System.Func`4<!!2,!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "AggregateBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!2,!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "AllAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "AllAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "AnyAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "AnyAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "AnyAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Decimal>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Double>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Int32>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Int64>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Decimal>>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Double>>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Int32>>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Int64>>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Single>>,System.Threading.CancellationToken)",
            "AverageAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Single>,System.Threading.CancellationToken)",
            "Cast``1(System.Collections.Generic.IAsyncEnumerable`1<System.Object>)",
            "Chunk``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32)",
            "ContainsAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!0,System.Collections.Generic.IEqualityComparer`1<!!0>,System.Threading.CancellationToken)",
            "CountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "CountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "CountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "CountBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "CountBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "DefaultIfEmpty``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>)",
            "DefaultIfEmpty``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!0)",
            "DistinctBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "DistinctBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "Distinct``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>)",
            "ElementAtAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Index,System.Threading.CancellationToken)",
            "ElementAtAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32,System.Threading.CancellationToken)",
            "ElementAtOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Index,System.Threading.CancellationToken)",
            "ElementAtOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32,System.Threading.CancellationToken)",
            "ExceptBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "ExceptBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "Except``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>)",
            "FirstAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "FirstAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "FirstAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!0,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,!!0,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,!!0,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "FirstOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "FullJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "FullJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "FullJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Func`3<!!0,!!1,!!3>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "FullJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "GroupBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`2<!!0,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`3<!!1,System.Collections.Generic.IEnumerable`1<!!0>,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`4<!!1,System.Collections.Generic.IEnumerable`1<!!0>,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`2<!!0,!!2>,System.Func`3<!!1,System.Collections.Generic.IEnumerable`1<!!2>,!!3>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupBy``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!1,System.Collections.Generic.IEnumerable`1<!!2>,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "GroupJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "GroupJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "GroupJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Func`3<!!0,System.Collections.Generic.IEnumerable`1<!!1>,!!3>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "GroupJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!0,System.Collections.Generic.IEnumerable`1<!!1>,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "Index``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>)",
            "InfiniteSequence``1(!!0,!!0)",
            "IntersectBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "IntersectBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "Intersect``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>)",
            "Join``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "Join``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "Join``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Func`3<!!0,!!1,!!3>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "Join``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "LastAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "LastAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "LastAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!0,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,!!0,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,!!0,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "LastOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "LeftJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "LeftJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "LeftJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Func`3<!!0,!!1,!!3>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "LeftJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "LongCountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "LongCountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "LongCountAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "MaxAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IComparer`1<!!0>,System.Threading.CancellationToken)",
            "MaxByAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IComparer`1<!!1>,System.Threading.CancellationToken)",
            "MaxByAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IComparer`1<!!1>,System.Threading.CancellationToken)",
            "MinAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IComparer`1<!!0>,System.Threading.CancellationToken)",
            "MinByAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IComparer`1<!!1>,System.Threading.CancellationToken)",
            "MinByAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IComparer`1<!!1>,System.Threading.CancellationToken)",
            "OfType``1(System.Collections.Generic.IAsyncEnumerable`1<System.Object>)",
            "Range(System.Int32,System.Int32)",
            "Repeat``1(!!0,System.Int32)",
            "Reverse``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>)",
            "RightJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "RightJoin``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "RightJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`2<!!0,!!2>,System.Func`2<!!1,!!2>,System.Func`3<!!0,!!1,!!3>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "RightJoin``4(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`3<!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!3>>,System.Collections.Generic.IEqualityComparer`1<!!2>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Collections.Generic.IAsyncEnumerable`1<!!1>>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Collections.Generic.IEnumerable`1<!!1>>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Collections.Generic.IAsyncEnumerable`1<!!1>>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Collections.Generic.IEnumerable`1<!!1>>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<!!1>>>)",
            "SelectMany``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<!!1>>>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Collections.Generic.IAsyncEnumerable`1<!!1>>,System.Func`3<!!0,!!1,!!2>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Collections.Generic.IAsyncEnumerable`1<!!1>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Collections.Generic.IEnumerable`1<!!1>>,System.Func`3<!!0,!!1,!!2>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Collections.Generic.IAsyncEnumerable`1<!!1>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Collections.Generic.IEnumerable`1<!!1>>,System.Func`3<!!0,!!1,!!2>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<!!1>>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>)",
            "SelectMany``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<!!1>>>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>)",
            "Select``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>)",
            "Select``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,!!1>)",
            "Select``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>)",
            "Select``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>)",
            "SequenceEqualAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>,System.Threading.CancellationToken)",
            "Sequence``1(!!0,!!0,!!0)",
            "Shuffle``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>)",
            "SingleAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "SingleAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "SingleAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,!!0,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,!!0,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,!!0,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>,System.Threading.CancellationToken)",
            "SingleOrDefaultAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "SkipLast``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32)",
            "SkipWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>)",
            "SkipWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Boolean>)",
            "SkipWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "SkipWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "Skip``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Decimal>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Double>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Int32>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Int64>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Decimal>>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Double>>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Int32>>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Int64>>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Nullable`1<System.Single>>,System.Threading.CancellationToken)",
            "SumAsync(System.Collections.Generic.IAsyncEnumerable`1<System.Single>,System.Threading.CancellationToken)",
            "TakeLast``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32)",
            "TakeWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>)",
            "TakeWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Boolean>)",
            "TakeWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "TakeWhile``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "Take``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Int32)",
            "Take``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Range)",
            "ToArrayAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "ToAsyncEnumerable``1(System.Collections.Generic.IEnumerable`1<!!0>)",
            "ToDictionaryAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToDictionaryAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToDictionaryAsync``2(System.Collections.Generic.IAsyncEnumerable`1<System.Collections.Generic.KeyValuePair`2<!!0,!!1>>,System.Collections.Generic.IEqualityComparer`1<!!0>,System.Threading.CancellationToken)",
            "ToDictionaryAsync``2(System.Collections.Generic.IAsyncEnumerable`1<System.ValueTuple`2<!!0,!!1>>,System.Collections.Generic.IEqualityComparer`1<!!0>,System.Threading.CancellationToken)",
            "ToDictionaryAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`2<!!0,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToDictionaryAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToHashSetAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>,System.Threading.CancellationToken)",
            "ToListAsync``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Threading.CancellationToken)",
            "ToLookupAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToLookupAsync``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToLookupAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Func`2<!!0,!!2>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "ToLookupAsync``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>,System.Collections.Generic.IEqualityComparer`1<!!1>,System.Threading.CancellationToken)",
            "UnionBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,!!1>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "UnionBy``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!1>>,System.Collections.Generic.IEqualityComparer`1<!!1>)",
            "Union``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IEqualityComparer`1<!!0>)",
            "Where``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`2<!!0,System.Boolean>)",
            "Where``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Int32,System.Boolean>)",
            "Where``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`3<!!0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "Where``1(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Func`4<!!0,System.Int32,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<System.Boolean>>)",
            "Zip``2(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>)",
            "Zip``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Collections.Generic.IAsyncEnumerable`1<!!2>)",
            "Zip``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`3<!!0,!!1,!!2>)",
            "Zip``3(System.Collections.Generic.IAsyncEnumerable`1<!!0>,System.Collections.Generic.IAsyncEnumerable`1<!!1>,System.Func`4<!!0,!!1,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask`1<!!2>>)");


        Add("System.Net.Http.HttpClient", EscapeReason.FrameworkAwait,
            "DeleteAsync(System.String)",
            "DeleteAsync(System.String,System.Threading.CancellationToken)",
            "DeleteAsync(System.Uri)",
            "DeleteAsync(System.Uri,System.Threading.CancellationToken)",
            "GetAsync(System.String)",
            "GetAsync(System.String,System.Net.Http.HttpCompletionOption)",
            "GetAsync(System.String,System.Net.Http.HttpCompletionOption,System.Threading.CancellationToken)",
            "GetAsync(System.String,System.Threading.CancellationToken)",
            "GetAsync(System.Uri)",
            "GetAsync(System.Uri,System.Net.Http.HttpCompletionOption)",
            "GetAsync(System.Uri,System.Net.Http.HttpCompletionOption,System.Threading.CancellationToken)",
            "GetAsync(System.Uri,System.Threading.CancellationToken)",
            "GetByteArrayAsync(System.String)",
            "GetByteArrayAsync(System.String,System.Threading.CancellationToken)",
            "GetByteArrayAsync(System.Uri)",
            "GetByteArrayAsync(System.Uri,System.Threading.CancellationToken)",
            "GetStreamAsync(System.String)",
            "GetStreamAsync(System.String,System.Threading.CancellationToken)",
            "GetStreamAsync(System.Uri)",
            "GetStreamAsync(System.Uri,System.Threading.CancellationToken)",
            "GetStringAsync(System.String)",
            "GetStringAsync(System.String,System.Threading.CancellationToken)",
            "GetStringAsync(System.Uri)",
            "GetStringAsync(System.Uri,System.Threading.CancellationToken)",
            "PatchAsync(System.String,System.Net.Http.HttpContent)",
            "PatchAsync(System.String,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "PatchAsync(System.Uri,System.Net.Http.HttpContent)",
            "PatchAsync(System.Uri,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "PostAsync(System.String,System.Net.Http.HttpContent)",
            "PostAsync(System.String,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "PostAsync(System.Uri,System.Net.Http.HttpContent)",
            "PostAsync(System.Uri,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "PutAsync(System.String,System.Net.Http.HttpContent)",
            "PutAsync(System.String,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "PutAsync(System.Uri,System.Net.Http.HttpContent)",
            "PutAsync(System.Uri,System.Net.Http.HttpContent,System.Threading.CancellationToken)",
            "SendAsync(System.Net.Http.HttpRequestMessage)",
            "SendAsync(System.Net.Http.HttpRequestMessage,System.Net.Http.HttpCompletionOption)",
            "SendAsync(System.Net.Http.HttpRequestMessage,System.Net.Http.HttpCompletionOption,System.Threading.CancellationToken)",
            "SendAsync(System.Net.Http.HttpRequestMessage,System.Threading.CancellationToken)");

        Add("System.Net.Http.HttpContent", EscapeReason.FrameworkAwait,
            "CopyToAsync(System.IO.Stream)",
            "CopyToAsync(System.IO.Stream,System.Net.TransportContext)",
            "CopyToAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)",
            "CopyToAsync(System.IO.Stream,System.Threading.CancellationToken)",
            "CreateContentReadStreamAsync()",
            "CreateContentReadStreamAsync(System.Threading.CancellationToken)",
            "LoadIntoBufferAsync()",
            "LoadIntoBufferAsync(System.Int64)",
            "LoadIntoBufferAsync(System.Int64,System.Threading.CancellationToken)",
            "LoadIntoBufferAsync(System.Threading.CancellationToken)",
            "ReadAsByteArrayAsync()",
            "ReadAsByteArrayAsync(System.Threading.CancellationToken)",
            "ReadAsStreamAsync()",
            "ReadAsStreamAsync(System.Threading.CancellationToken)",
            "ReadAsStringAsync()",
            "ReadAsStringAsync(System.Threading.CancellationToken)");

        Add("System.Net.Http.HttpMessageInvoker", EscapeReason.FrameworkAwait,
            "SendAsync(System.Net.Http.HttpRequestMessage,System.Threading.CancellationToken)");

        Add("System.Net.Http.MessageProcessingHandler", EscapeReason.AffinityBackstop,
            "SendAsync(System.Net.Http.HttpRequestMessage,System.Threading.CancellationToken)");

        Add("System.Net.Http.MultipartContent", EscapeReason.FrameworkAwait,
            "CreateContentReadStream(System.Threading.CancellationToken)",
            "CreateContentReadStreamAsync()",
            "CreateContentReadStreamAsync(System.Threading.CancellationToken)",
            "SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext)",
            "SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)");

        Add("System.Net.Http.MultipartFormDataContent", EscapeReason.FrameworkAwait,
            "SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)");

        Add("System.Net.Http.StreamContent", EscapeReason.FrameworkAwait,
            "SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext)",
            "SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)");

        Add("System.Net.Sockets.Socket", EscapeReason.OsResource,
            "BeginAccept(System.Int32,System.AsyncCallback,System.Object)",
            "BeginAccept(System.Net.Sockets.Socket,System.Int32,System.AsyncCallback,System.Object)",
            "BeginConnect(System.Net.EndPoint,System.AsyncCallback,System.Object)",
            "BeginConnect(System.Net.IPAddress,System.Int32,System.AsyncCallback,System.Object)",
            "BeginConnect(System.Net.IPAddress[],System.Int32,System.AsyncCallback,System.Object)",
            "BeginConnect(System.String,System.Int32,System.AsyncCallback,System.Object)",
            "ConnectAsync(System.Net.EndPoint)",
            "ConnectAsync(System.Net.EndPoint,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.IPAddress,System.Int32)",
            "ConnectAsync(System.Net.IPAddress,System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.IPAddress[],System.Int32)",
            "ConnectAsync(System.Net.IPAddress[],System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.Sockets.SocketAsyncEventArgs)",
            "ConnectAsync(System.Net.Sockets.SocketType,System.Net.Sockets.ProtocolType,System.Net.Sockets.SocketAsyncEventArgs)",
            "ConnectAsync(System.Net.Sockets.SocketType,System.Net.Sockets.ProtocolType,System.Net.Sockets.SocketAsyncEventArgs,System.Net.Sockets.ConnectAlgorithm)",
            "ConnectAsync(System.String,System.Int32)",
            "ConnectAsync(System.String,System.Int32,System.Threading.CancellationToken)",
            "SendFileAsync(System.String,System.ReadOnlyMemory`1<System.Byte>,System.ReadOnlyMemory`1<System.Byte>,System.Net.Sockets.TransmitFileOptions,System.Threading.CancellationToken)",
            "SendFileAsync(System.String,System.Threading.CancellationToken)",
            "SendPacketsAsync(System.Net.Sockets.SocketAsyncEventArgs)");

        Add("System.Net.Sockets.SocketTaskExtensions", EscapeReason.OsResource,
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.EndPoint)",
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.EndPoint,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.IPAddress,System.Int32)",
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.IPAddress,System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.IPAddress[],System.Int32)",
            "ConnectAsync(System.Net.Sockets.Socket,System.Net.IPAddress[],System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.Sockets.Socket,System.String,System.Int32)",
            "ConnectAsync(System.Net.Sockets.Socket,System.String,System.Int32,System.Threading.CancellationToken)");

        Add("System.Net.Sockets.TcpClient", EscapeReason.OsResource,
            "BeginConnect(System.Net.IPAddress[],System.Int32,System.AsyncCallback,System.Object)",
            "ConnectAsync(System.Net.IPAddress,System.Int32)",
            "ConnectAsync(System.Net.IPAddress,System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.IPAddress[],System.Int32)",
            "ConnectAsync(System.Net.IPAddress[],System.Int32,System.Threading.CancellationToken)",
            "ConnectAsync(System.Net.IPEndPoint)",
            "ConnectAsync(System.Net.IPEndPoint,System.Threading.CancellationToken)",
            "ConnectAsync(System.String,System.Int32)",
            "ConnectAsync(System.String,System.Int32,System.Threading.CancellationToken)");

        Add("System.Net.Sockets.TcpListener", EscapeReason.OsResource,
            "AcceptTcpClientAsync()",
            "AcceptTcpClientAsync(System.Threading.CancellationToken)",
            "BeginAcceptTcpClient(System.AsyncCallback,System.Object)");

        Add("System.Net.Sockets.UdpClient", EscapeReason.OsResource,
            "ReceiveAsync()",
            "ReceiveAsync(System.Threading.CancellationToken)");

        Add("System.Progress`1", EscapeReason.AffinityBackstop,
            "OnReport(!0)");

        Add("System.Runtime.CompilerServices.AsyncIteratorMethodBuilder", EscapeReason.UnhandledException,
            "AwaitOnCompleted``2(!!0&,!!1&)",
            "AwaitUnsafeOnCompleted``2(!!0&,!!1&)");

        Add("System.Runtime.CompilerServices.AsyncVoidMethodBuilder", EscapeReason.UnhandledException,
            "AwaitOnCompleted``2(!!0&,!!1&)",
            "AwaitUnsafeOnCompleted``2(!!0&,!!1&)",
            "SetException(System.Exception)",
            "SetResult()");

        Add("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder", EscapeReason.ValueTaskSource,
            "SetException(System.Exception)",
            "SetResult()");

        Add("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder", EscapeReason.UnhandledException,
            "AwaitOnCompleted``2(!!0&,!!1&)",
            "AwaitUnsafeOnCompleted``2(!!0&,!!1&)");

        Add("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1", EscapeReason.ValueTaskSource,
            "SetException(System.Exception)",
            "SetResult(!0)");

        Add("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1", EscapeReason.UnhandledException,
            "AwaitOnCompleted``2(!!0&,!!1&)",
            "AwaitUnsafeOnCompleted``2(!!0&,!!1&)");

        Add("System.Threading.CancellationTokenRegistration", EscapeReason.FrameworkAwait,
            "DisposeAsync()");

        Add("System.Threading.Tasks.ConcurrentExclusiveSchedulerPair", EscapeReason.CustomScheduler,
            ".ctor()",
            "Complete()");

        Add("System.Threading.Tasks.ParallelOptions", EscapeReason.SchedulerValue,
            ".ctor()");

        Add("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1", EscapeReason.ValueTaskSource,
            "OnCompleted(System.Action`1<System.Object>,System.Object,System.Int16,System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags)",
            "SetException(System.Exception)",
            "SetResult(!0)");

        Add("System.Threading.Tasks.TaskScheduler", EscapeReason.SchedulerValue,
            "get_Current()");

        Add("System.Threading.Tasks.TaskScheduler", EscapeReason.CustomScheduler,
            "TryExecuteTask(System.Threading.Tasks.Task)");

        Add("System.Threading.Tasks.TaskToAsyncResult", EscapeReason.FrameworkAwait,
            "Begin(System.Threading.Tasks.Task,System.AsyncCallback,System.Object)");

        return members.ToImmutable();
    }
}
