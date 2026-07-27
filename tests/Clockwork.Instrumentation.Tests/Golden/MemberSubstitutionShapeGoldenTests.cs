using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>Verifies member substitution selects methods by complete signature shape.</summary>
public sealed class MemberSubstitutionShapeGoldenTests
{
    private const string Fixture = """
        using System.Collections.Generic;
        using ClockworkFixtures.Api;

        namespace Fx
        {
            public static class ShapeCalls
            {
                public static int Run()
                {
                    var value = new LegacyShapes();
                    int number = 1;
                    string text = "x";
                    long wide = 2;
                    int sum = value.Choose(number);
                    sum += value.Ref(ref number);
                    sum += value.In(in wide);
                    sum += value.Out(out text);
                    sum += value.Generic(new List<int> { number });
                    sum += value.NestedShape(new LegacyShapes.Nested());
                    return sum;
                }
            }
        }
        """;

    [Fact]
    public void SameArityOverloadsMapByRefGenericAndNestedShape()
    {
        using var context = RewriteTestContext.Create();
        string fixturePath = context.CompileFixture("Fx.MemberShapes", Fixture);
        var rules = new RewriteRuleSet(
            "clockwork.member-shapes",
            "1.0",
            [
                RewriteRule.SubstituteType(
                    "shape-type",
                    "ClockworkFixtures.Api.LegacyShapes",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernShapes")),
                RewriteRule.SubstituteType(
                    "shape-nested",
                    "ClockworkFixtures.Api.LegacyShapes/Nested",
                    RewriteReplacement.Type(FixtureSources.ShimAssemblyName, "ClockworkFixtures.Shims.ModernShapes/Nested")),
            ]);

        RewriteResult result = context.Rewrite(fixturePath, rules);
        result.EnsureSuccess();

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.MemberShapes.rewritten.dll"));
        MethodDefinition run = CecilInspect.GetMethod(module, "Fx.ShapeCalls", "Run");
        MethodReference[] calls = run.Body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(method => method.DeclaringType.FullName.Contains("ModernShapes", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(calls, method => HasParameters(method, "System.Int32"));
        Assert.Contains(calls, method => HasParameters(method, "System.Int32&"));
        Assert.Contains(calls, method => HasParameters(method, "System.Int64&"));
        Assert.Contains(calls, method => HasParameters(method, "System.String&"));
        Assert.Contains(
            calls,
            method => method is GenericInstanceMethod generic
                && generic.GenericArguments.Single().FullName == "System.Int32"
                && HasParameters(generic.ElementMethod, "System.Collections.Generic.List`1<!!0>"));
        Assert.Contains(calls, method => HasParameters(method, "ClockworkFixtures.Shims.ModernShapes/Nested"));
        Assert.DoesNotContain(calls, method => HasParameters(method, "System.String") && method.Name == "Choose");
        Assert.DoesNotContain(calls, method => HasParameters(method, "System.Int32&") && method.Name == "Out");
        Assert.All(result.Manifest.Transformations, transformation =>
        {
            Assert.True(transformation.ILOffset > 0);
            Assert.NotNull(transformation.SourceFile);
            Assert.True(transformation.SourceLine > 0);
        });
    }

    private static bool HasParameters(MethodReference method, params string[] parameterTypes) =>
        method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes);
}
