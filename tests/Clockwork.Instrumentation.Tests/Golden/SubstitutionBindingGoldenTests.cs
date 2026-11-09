using System.Reflection;
using System.Runtime.Loader;
using Clockwork.Instrumentation.Diagnostics;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Golden tests for the rewrite-time check that fails a rewrite (<c>CWR0001</c>) instead of emitting a member reference
/// that does not bind: a substituted signature binds only where the declaring type is rewritten too, and code excluded
/// from rewriting keeps the original signatures, so neither side may reference the other's rewritten members. The
/// closure-level cases, where the two sides are different assemblies, are in <c>ClosureBindingTests</c>.
/// </summary>
public sealed class SubstitutionBindingGoldenTests
{
    private const string DoNotRewriteAttributeSource = """
        namespace Clockwork.Instrumentation.Attributes
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class DoNotRewriteAttribute(string reason) : System.Attribute
            {
                public string Reason { get; } = reason;
            }
        }
        """;

    public static TheoryData<string, string, string> UnboundCalls => new()
    {
        // Without a closure, no other assembly is known to be rewritten, so a sibling keeps its original signature.
        { "Sibling", "public static string Call(LegacyMarker marker) => MarkerSink.Describe(marker);", "MarkerSink::Describe" },

        // A replacement assembly is never rewritten, so the substituted signature does not exist there.
        { "Replacement", "public static string Call(LegacyMarker marker) => MarkerShim.Describe(marker);", "MarkerShim::Describe" },
        { "ReplacementGeneric", "public static LegacyMarker Call(LegacyMarker marker) => MarkerShim.Make(marker);", "MarkerShim::Make" },

        // A type excluded from rewriting keeps its original signature.
        { "Excluded", "public static string Call(LegacyMarker marker) => Excluded.Describe(marker);", "Fx.Excluded::Describe" },
    };

    public static TheoryData<string, string, string> ExcludedCallers => new()
    {
        // A definition token binds to the rewritten signature, which would receive an argument of the original type.
        { "NonGeneric", "Api.Take(marker)", "Fx.Api::Take" },
        { "GenericMethod", "Api.TakeGeneric(marker, 1)", "Fx.Api::TakeGeneric" },

        // A member of a generic instance binds by its original signature, which the rewritten type no longer declares.
        { "GenericType", "Box<int>.Take(marker)", "Fx.Box`1::Take" },
    };

    [Theory]
    [MemberData(nameof(UnboundCalls))]
    public void SubstitutedSignatureFailsWhereItsDeclaringTypeIsNotRewritten(string name, string call, string unbound)
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture($"Fx.Binding{name}", $$"""
            using ClockworkFixtures.Api;
            using ClockworkFixtures.Shims;

            namespace Fx
            {
                public static class Excluded
                {
                    public static string Describe(LegacyMarker marker) => marker.ToString();
                }

                public static class Caller
                {
                    {{call}}
                }
            }
            """);

        RewriteResult result = context.Rewrite(fixturePath, MarkerRuleSet(), MarkerOptions(context, excluded: "Fx.Excluded"));

        RewriteDiagnostic error = AssertUnbound(result, unbound);
        Assert.Equal("Fx.Caller.Call", error.Method);
    }

    [Theory]
    [MemberData(nameof(ExcludedCallers))]
    public void ExcludedCallerOfARewrittenSignatureFailsTheRewrite(string name, string call, string unbound)
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture($"Fx.ExcludedCaller{name}", $$"""
            using ClockworkFixtures.Api;

            {{DoNotRewriteAttributeSource}}

            namespace Fx
            {
                public static class Api
                {
                    public static string Take(LegacyMarker marker) => marker.ToString();

                    public static string TakeGeneric<T>(LegacyMarker marker, T value) => marker + " " + value;
                }

                public static class Box<T>
                {
                    public static string Take(LegacyMarker marker) => marker.ToString();
                }

                [Clockwork.Instrumentation.Attributes.DoNotRewrite("Runs before the simulation starts.")]
                public static class Legacy
                {
                    public static string Call(LegacyMarker marker) => {{call}};
                }
            }
            """);

        RewriteResult result = context.Rewrite(fixturePath, MarkerRuleSet(), MarkerOptions(context));

        RewriteDiagnostic error = AssertUnbound(result, unbound);
        Assert.Equal("Fx.Legacy.Call", error.Method);
        Assert.Contains("'Fx.Legacy' is excluded from rewriting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RewrittenCodeUsingAFieldOfAnExcludedTypeFailsTheRewrite()
    {
        // The excluded type keeps a real Lock in its field, which the rewritten lock statement would enter as a
        // ControlledLock.
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.ExcludedField", $$"""
            using System.Threading;

            {{DoNotRewriteAttributeSource}}

            namespace Fx
            {
                [Clockwork.Instrumentation.Attributes.DoNotRewrite("Shared with code outside the simulation.")]
                public static class Globals
                {
                    public static readonly Lock Gate = new();
                }

                public static class Locker
                {
                    public static int Run()
                    {
                        lock (Globals.Gate)
                        {
                            return 1;
                        }
                    }
                }
            }
            """);
        string runtimePath = typeof(ControlledLock).Assembly.Location;
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [runtimePath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(runtimePath)!],
        };

        RewriteResult result = context.Rewrite(
            fixturePath,
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies),
            options);

        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Equal("Fx.Locker.Run", error.Method);
        Assert.Contains("'clockwork.lock.type'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Fx.Globals::Gate", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Fx.Globals', which is excluded from rewriting", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExcludedTypeIsNotRewrittenWithItsCompilerGeneratedTypes(bool byAttribute)
    {
        // The async method's state machine, the iterator, the lambda closures, and the async lambda's state machine
        // nested in its closure are compiler-generated types nested in the excluded type, so they are excluded with
        // it and keep the BCL machinery its stub methods use. An explicitly declared nested type is rewritten.
        using var context = RewriteTestContext.Create();
        string attribute = byAttribute ? "[Clockwork.Instrumentation.Attributes.DoNotRewrite(\"Runs outside the simulation.\")]" : string.Empty;
        string fixturePath = context.CompileFixture("Fx.ExcludedHelpers", $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            {{DoNotRewriteAttributeSource}}

            namespace Fx
            {
                {{attribute}}
                public static class Legacy
                {
                    public static async Task<int> AwaitAsync()
                    {
                        await Task.Yield();
                        return 1;
                    }

                    public static IEnumerable<int> Iterate()
                    {
                        yield return 1;
                        yield return 2;
                    }

                    public static int Lambda(int offset)
                    {
                        Func<int, int> add = value => value + offset;
                        Func<int, int> twice = static value => value * 2;
                        Func<Task<int>> later = async () =>
                        {
                            await Task.Yield();
                            return add(1);
                        };
                        return twice(later().GetAwaiter().GetResult());
                    }

                    public static class Declared
                    {
                        public static async Task<int> AwaitAsync()
                        {
                            await Task.Yield();
                            return 2;
                        }
                    }
                }
            }
            """);
        string runtimePath = typeof(ControlledLock).Assembly.Location;
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [runtimePath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(runtimePath)!],
            ExcludedTypeFullNames = byAttribute ? [] : ["Fx.Legacy"],
        };

        RewriteResult result = context.Rewrite(
            fixturePath,
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies),
            options);

        result.EnsureSuccess();
        using ModuleDefinition module = context.LoadModule(Path.Combine(context.Directory, "Fx.ExcludedHelpers.rewritten.dll"));
        TypeDefinition legacy = module.GetType("Fx.Legacy");
        TypeDefinition declared = legacy.NestedTypes.Single(type => type.Name == "Declared");
        TypeDefinition[] generated = [.. NestedTypes(legacy).Except([declared, .. NestedTypes(declared)])];
        string[] excluded = [.. result.Manifest.Exclusions.Select(exclusion => exclusion.TypeFullName)];

        Assert.Contains(generated, type => type.Name.StartsWith("<AwaitAsync>", StringComparison.Ordinal));
        Assert.Contains(generated, type => type.Name.StartsWith("<Iterate>", StringComparison.Ordinal));
        Assert.Contains(generated, type => type.Name == "<>c");
        Assert.Contains(generated, type => type.DeclaringType.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal));
        Assert.Equal(
            generated.Select(type => type.FullName).Append("Fx.Legacy").Order(StringComparer.Ordinal),
            excluded.Order(StringComparer.Ordinal));
        Assert.All(generated.Where(IsAsyncStateMachine), type => Assert.StartsWith(
            "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1",
            type.Fields.Single(field => field.Name == "<>t__builder").FieldType.FullName,
            StringComparison.Ordinal));
        Assert.All(NestedTypes(declared).Where(IsAsyncStateMachine), type => Assert.StartsWith(
            "Clockwork.Shims.System.Runtime.CompilerServices.ControlledAsyncTaskMethodBuilder`1",
            type.Fields.Single(field => field.Name == "<>t__builder").FieldType.FullName,
            StringComparison.Ordinal));
        Assert.Equal(2, generated.Count(IsAsyncStateMachine));
        Assert.Single(NestedTypes(declared), IsAsyncStateMachine);
    }

    [Fact]
    public void RewrittenCallerCrossingIntoAnExcludedTypeWithASubstitutedTypeFailsTheRewrite()
    {
        // The excluded type and its state machine are not rewritten, so its method still returns the BCL configured
        // awaitable, which the rewritten caller would await as the controlled one.
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.ExcludedBoundary", $$"""
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            {{DoNotRewriteAttributeSource}}

            namespace Fx
            {
                [Clockwork.Instrumentation.Attributes.DoNotRewrite("Runs outside the simulation.")]
                public static class Legacy
                {
                    public static async Task<int> AwaitAsync()
                    {
                        await Task.Yield();
                        return 1;
                    }

                    public static ConfiguredTaskAwaitable<int> Configured() => AwaitAsync().ConfigureAwait(false);
                }

                public static class Caller
                {
                    public static async Task<int> Run() => await Legacy.Configured();
                }
            }
            """);
        string runtimePath = typeof(ControlledLock).Assembly.Location;
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [runtimePath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(runtimePath)!],
        };

        RewriteResult result = context.Rewrite(
            fixturePath,
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies),
            options);

        Assert.False(result.Succeeded);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.StartsWith("Fx.Caller/<Run>", error.Method, StringComparison.Ordinal);
        Assert.Contains("'clockwork.tasks.configured.awaitable.generic'", error.Message, StringComparison.Ordinal);
        Assert.Contains("Fx.Legacy::Configured()", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Fx.Legacy' is excluded from rewriting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultidimensionalArrayOfASubstitutedTypeBinds()
    {
        // The runtime provides an array type's constructor and accessors, so they bind with the substituted element type.
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.Arrays", """
            using ClockworkFixtures.Api;

            namespace Fx
            {
                public static class Arrays
                {
                    public static string Describe()
                    {
                        var markers = new LegacyMarker[1, 1];
                        markers[0, 0] = new LegacyMarker();
                        return markers[0, 0].ToString();
                    }
                }
            }
            """);

        context.Rewrite(fixturePath, MarkerRuleSet(), MarkerOptions(context)).EnsureSuccess();

        string rewrittenPath = Path.Combine(context.Directory, "Fx.Arrays.rewritten.dll");
        using (ModuleDefinition module = context.LoadModule(rewrittenPath))
        {
            List<string> targets = CecilInspect.CallTargets(CecilInspect.GetMethod(module, "Fx.Arrays", "Describe"));
            Assert.Contains(targets, target => target.Contains("ClockworkFixtures.Shims.ModernMarker[", StringComparison.Ordinal)
                && target.EndsWith("]::Get(System.Int32,System.Int32)", StringComparison.Ordinal));
            Assert.DoesNotContain(targets, target => target.Contains("LegacyMarker", StringComparison.Ordinal));
        }

        var loadContext = new DirectoryLoadContext(context.Directory);
        try
        {
            Type arrays = loadContext.LoadFromAssemblyPath(rewrittenPath).GetType("Fx.Arrays", throwOnError: true)!;
            Assert.Equal("modern", arrays.GetMethod("Describe")!.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static IEnumerable<TypeDefinition> NestedTypes(TypeDefinition type) =>
        type.NestedTypes.SelectMany(nested => NestedTypes(nested).Prepend(nested));

    private static bool IsAsyncStateMachine(TypeDefinition type) =>
        type.Interfaces.Any(implementation =>
            implementation.InterfaceType.FullName == "System.Runtime.CompilerServices.IAsyncStateMachine");

    private static RewriteDiagnostic AssertUnbound(RewriteResult result, string unbound)
    {
        Assert.False(result.Succeeded, "The rewrite succeeded: " + string.Join(" | ", result.Diagnostics));
        Assert.False(result.WasWritten);
        RewriteDiagnostic error = Assert.Single(result.Errors);
        Assert.Equal(RewriteDiagnosticIds.UnresolvedReplacement, error.Id);
        Assert.Contains("'substitute-marker'", error.Message, StringComparison.Ordinal);
        Assert.Contains(unbound, error.Message, StringComparison.Ordinal);
        return error;
    }

    private static RewriteRuleSet MarkerRuleSet() =>
        new(
            "clockwork.fixtures.binding",
            "1.0",
            [
                RewriteRule.SubstituteType(
                    "substitute-marker",
                    "ClockworkFixtures.Api.LegacyMarker",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernMarker")),
            ]);

    private static RewriteOptions MarkerOptions(RewriteTestContext context, string? excluded = null) =>
        new()
        {
            ReplacementAssemblyPaths = [context.ShimPath],
            ReferenceSearchDirectories = [context.Directory],
            ExcludedTypeFullNames = excluded is null ? [] : [excluded],
        };

    private sealed class DirectoryLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }
    }
}
