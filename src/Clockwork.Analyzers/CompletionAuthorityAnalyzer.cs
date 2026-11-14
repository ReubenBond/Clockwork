using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Clockwork.Analyzers;

/// <summary>
/// <para>
/// Enforces, in the Clockwork runtime assembly, that every completion Clockwork performs for simulation code runs through
/// <c>Clockwork.Runtime.Tasks.SimulationCompletion</c>. The BCL runs a completed task's continuations where the completing
/// call runs, and queues them to the real thread pool unless no synchronization context and the default scheduler are
/// current; <c>SimulationCompletion</c> establishes that, so a completion member called anywhere else can send simulation
/// work to the thread pool.
/// </para>
/// <para>
/// <c>CW1004</c> reports a call of a completion member (<c>TaskCompletionSource</c>'s and
/// <c>TaskCompletionSource&lt;TResult&gt;</c>'s <c>Set*</c> and <c>TrySet*</c>,
/// <c>ManualResetValueTaskSourceCore&lt;TResult&gt;</c>'s <c>SetResult</c> and <c>SetException</c>, and
/// <c>CancellationTokenSource</c>'s <c>Cancel</c>) unless it is inside
/// <c>SimulationCompletion</c>, or directly inside a lambda passed to a parameter marked
/// <c>[CompletionCallback]</c>, which runs only through <c>SimulationCompletion</c>. The rule is active only in the
/// assembly which defines <c>SimulationCompletion</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompletionAuthorityAnalyzer : DiagnosticAnalyzer
{
    private const string AuthorityTypeName = "Clockwork.Runtime.Tasks.SimulationCompletion";
    private const string CallbackAttributeName = "CompletionCallbackAttribute";

    private static readonly ImmutableHashSet<string> TaskCompletionSourceMembers = ImmutableHashSet.Create(
        "SetResult",
        "TrySetResult",
        "SetException",
        "TrySetException",
        "SetCanceled",
        "TrySetCanceled",
        "SetFromTask",
        "TrySetFromTask");

    private static readonly ImmutableHashSet<string> ValueTaskSourceCoreMembers = ImmutableHashSet.Create("SetResult", "SetException");

    /// <summary>
    /// <c>CW1004</c>: Clockwork code calls a completion member outside <c>SimulationCompletion</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor CompletionOutsideAuthority = new(
        id: "CW1004",
        title: "Clockwork completes tasks through SimulationCompletion",
        messageFormat: "'{0}' completes a task, or cancels a token, outside SimulationCompletion; call it in a lambda passed to SimulationCompletion.Run",
        category: "Clockwork.Runtime",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every completion Clockwork performs for simulation code runs through SimulationCompletion, with no synchronization context and the default scheduler current, so the BCL runs the continuations it completes inline on the simulation's logical thread rather than queuing them to the real thread pool.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [CompletionOutsideAuthority];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? authority = context.Compilation.GetTypeByMetadataName(AuthorityTypeName);
        if (authority is null || !SymbolEqualityComparer.Default.Equals(authority.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        INamedTypeSymbol?[] taskCompletionSources =
        [
            context.Compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskCompletionSource"),
            context.Compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskCompletionSource`1"),
        ];
        INamedTypeSymbol? valueTaskSourceCore =
            context.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1");
        INamedTypeSymbol? cancellationTokenSource = context.Compilation.GetTypeByMetadataName("System.Threading.CancellationTokenSource");

        bool IsCompletionMember(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType.OriginalDefinition;
            if (taskCompletionSources.Any(candidate => SymbolEqualityComparer.Default.Equals(type, candidate)))
            {
                return TaskCompletionSourceMembers.Contains(method.Name);
            }

            if (SymbolEqualityComparer.Default.Equals(type, valueTaskSourceCore))
            {
                return ValueTaskSourceCoreMembers.Contains(method.Name);
            }

            return SymbolEqualityComparer.Default.Equals(type, cancellationTokenSource) && method.Name == "Cancel";
        }

        context.RegisterOperationAction(
            operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;
                if (IsCompletionMember(invocation.TargetMethod)
                    && !IsAuthorized(invocation, operationContext.ContainingSymbol, authority))
                {
                    operationContext.ReportDiagnostic(Diagnostic.Create(
                        CompletionOutsideAuthority,
                        invocation.Syntax.GetLocation(),
                        invocation.TargetMethod.ContainingType.Name + "." + invocation.TargetMethod.Name));
                }
            },
            OperationKind.Invocation);
    }

    private static bool IsAuthorized(IOperation invocation, ISymbol containingSymbol, INamedTypeSymbol authority)
    {
        for (IOperation? operation = invocation.Parent; operation is not null; operation = operation.Parent)
        {
            if (operation is IAnonymousFunctionOperation function)
            {
                return IsPassedAsCompletionCallback(function);
            }
        }

        for (INamedTypeSymbol? type = containingSymbol.ContainingType; type is not null; type = type.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, authority))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPassedAsCompletionCallback(IAnonymousFunctionOperation function)
    {
        IOperation? parent = function.Parent;
        while (parent is IDelegateCreationOperation or IConversionOperation)
        {
            parent = parent.Parent;
        }

        return parent is IArgumentOperation { Parameter: { } parameter }
            && parameter.OriginalDefinition.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == CallbackAttributeName);
    }
}
