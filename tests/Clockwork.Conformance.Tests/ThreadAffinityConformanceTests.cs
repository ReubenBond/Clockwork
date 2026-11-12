using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Runtime.Policy;
using Clockwork.Runtime.Shims;
using Mono.Cecil;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// Conformance for the controlled APIs' thread affinity: while a simulation is active, every controlled rewrite target
/// requires the simulation's logical thread. A thread which carries the simulation's ambient context through code
/// Clockwork did not rewrite - the real <c>Task.Run</c> in <c>BackgroundService.StartAsync</c>, say - is rejected with a
/// <see cref="SimulationThreadAffinityException"/> naming the API, and the thread driving the simulation surfaces the
/// violation too.
/// </summary>
public sealed class ThreadAffinityConformanceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan EscapedWorkTimeout = TimeSpan.FromSeconds(30);

    private const string HostedServiceSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Extensions.Hosting;

        namespace Conf {
        public sealed class Worker : BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken stoppingToken) => await Task.Delay(1, stoppingToken);
        }

        public static class HostedServiceProbe
        {
            public static Task Start()
            {
                var worker = new Worker();
                _ = worker.StartAsync(CancellationToken.None);
                return worker.ExecuteTask!;
            }
        } }
        """;

    private readonly RewriteFixture _fixture = new();

    /// <summary>
    /// Every target of the built-in controlled rules - each redirected method and each public member of a substituted
    /// type - is invoked with default arguments on a thread which carries the simulation's ambient context and must
    /// reject it before reading those arguments. The entry points come from the shipped rule inventories, so a new rule
    /// is covered when it is added.
    /// </summary>
    [Fact]
    public async Task EveryControlledEntryPointRejectsAThreadOutsideTheSimulation()
    {
        MethodBase[] entryPoints = ControlledEntryPoints();
        Assert.NotEmpty(entryPoints);
        var cluster = new SimulationCluster(seed: 1, Start);
        ExecutionContext ambient = CaptureSimulationContext(cluster);
        var failures = new List<string>();

        RunOnEscapedThread(ambient, () =>
        {
            foreach (MethodBase entryPoint in entryPoints)
            {
                Exception? exception = Record.Exception(() => InvokeWithDefaultArguments(entryPoint));
                if (exception is not SimulationThreadAffinityException { ApiName.Length: > 0 })
                {
                    failures.Add($"{entryPoint.DeclaringType}::{entryPoint}: {exception?.GetType().Name ?? "returned"}");
                }
            }
        });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Throws<SimulationThreadAffinityException>(() => cluster.RunUntilIdle(TestContext.Current.CancellationToken));
        await cluster.DisposeAsync();
    }

    /// <summary>
    /// The motivating case: <c>BackgroundService</c> loads from the shared framework, uninstrumented, and its
    /// <c>StartAsync</c> starts the rewritten <c>ExecuteAsync</c> through the real <c>Task.Run</c>. The escaped thread fails
    /// at its first controlled call, and although nothing observes the faulted task, the driver surfaces the violation.
    /// </summary>
    [Fact]
    public async Task BackgroundServiceExecuteAsyncEscapingToTheThreadPoolFailsTheRun()
    {
        StagedProbe probe = _fixture.StageControlledTasks(
            "Conf.ThreadAffinity.HostedService",
            "Conf.HostedServiceProbe",
            HostedServiceSource,
            optimize: true);
        var cluster = new SimulationCluster(seed: 1, Start);
        Task? executeTask = null;
        cluster.SchedulerLane.Enqueue(() => executeTask = (Task)probe.Method("Start").Invoke(null, null)!);

        // The escaped thread races this drive, so the violation surfaces from whichever drive follows its report.
        Exception? during = Record.Exception(() => cluster.RunUntilIdle(TestContext.Current.CancellationToken));
        Assert.NotNull(executeTask);
        Assert.True(SpinWait.SpinUntil(() => executeTask.IsCompleted, EscapedWorkTimeout));
        Exception? after = Record.Exception(() => cluster.RunUntilIdle(TestContext.Current.CancellationToken));

        var escaped = Assert.IsType<SimulationThreadAffinityException>(executeTask.Exception?.InnerException);
        Assert.Equal("System.Runtime.CompilerServices.AsyncTaskMethodBuilder", escaped.ApiName);
        Assert.NotEqual(Environment.CurrentManagedThreadId, escaped.ManagedThreadId);
        var surfaced = Assert.Single(new[] { during, after }.OfType<Exception>());
        Assert.Equal(escaped.ApiName, Assert.IsType<SimulationThreadAffinityException>(surfaced).ApiName);
        await cluster.DisposeAsync();
    }

    public void Dispose() => _fixture.Dispose();

    private static ExecutionContext CaptureSimulationContext(SimulationCluster cluster)
    {
        ExecutionContext? captured = null;
        cluster.SchedulerLane.Enqueue(() => captured = ExecutionContext.Capture());
        cluster.RunUntilIdle(TestContext.Current.CancellationToken);
        return captured ?? throw new InvalidOperationException("The simulation work did not capture its context.");
    }

    private static void RunOnEscapedThread(ExecutionContext ambient, Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ExecutionContext.Run(ambient, static state => ((Action)state!)(), body);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        Assert.True(thread.Join(EscapedWorkTimeout), "An entry point blocked the escaped thread.");
        Assert.Null(failure);
    }

    private static MethodBase[] ControlledEntryPoints()
    {
        Module shims = typeof(SimulationRuntimeDispatch).Module;
        using ModuleDefinition module = ModuleDefinition.ReadModule(shims.Assembly.Location);
        var entryPoints = new Dictionary<int, MethodBase>();
        foreach ((_, RewriteRule rule) in BuiltInRuleSets.DeterministicBclInventory.Concat(BuiltInRuleSets.ControlledTasksInventory))
        {
            // Rejected APIs throw wherever they run; injected scheduling points are not controlled APIs.
            if (rule.Policy != SimulationApiPolicy.Controlled
                || rule.Operation is RewriteOperationKind.InjectRejection or RewriteOperationKind.InjectSchedulingPoint)
            {
                continue;
            }

            TypeDefinition type = module.GetType(rule.Replacement.DeclaringTypeFullName)
                ?? throw new InvalidOperationException($"Shim type '{rule.Replacement.DeclaringTypeFullName}' is missing.");
            IEnumerable<MethodDefinition> targets = rule.Operation == RewriteOperationKind.SubstituteType
                ? type.Methods.Where(static method => method.IsPublic)
                : type.Methods.Where(method => method.IsStatic
                    && method.IsPublic
                    && method.Name == rule.Replacement.MemberName
                    && ParametersMatch(method, rule.Replacement.ParameterTypeFullNames));
            foreach (MethodDefinition target in targets)
            {
                int token = target.MetadataToken.ToInt32();
                entryPoints.TryAdd(token, Close(shims.ResolveMethod(token)!));
            }
        }

        return [.. entryPoints.Values];
    }

    private static bool ParametersMatch(MethodDefinition method, ImmutableArray<string> parameterTypeFullNames) =>
        parameterTypeFullNames.IsDefault
        || method.Parameters.Select(static parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypeFullNames);

    private static MethodBase Close(MethodBase method)
    {
        if (method.DeclaringType is { IsGenericTypeDefinition: true } open)
        {
            Type closed = open.MakeGenericType([.. open.GetGenericArguments().Select(ChooseTypeArgument)]);
            method = MethodBase.GetMethodFromHandle(method.MethodHandle, closed.TypeHandle)!;
        }

        return method is MethodInfo { IsGenericMethodDefinition: true } generic
            ? generic.MakeGenericMethod([.. generic.GetGenericArguments().Select(ChooseTypeArgument)])
            : method;
    }

    private static Type ChooseTypeArgument(Type parameter)
    {
        Type[] candidates = [typeof(int), typeof(object), typeof(StateMachineStub), typeof(AwaiterStub)];
        return candidates.FirstOrDefault(candidate => Satisfies(candidate, parameter))
            ?? throw new InvalidOperationException($"No stub type satisfies the constraints of '{parameter}'.");
    }

    private static bool Satisfies(Type candidate, Type parameter)
    {
        GenericParameterAttributes attributes = parameter.GenericParameterAttributes;
        if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) && candidate.IsValueType)
        {
            return false;
        }

        if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) && !candidate.IsValueType)
        {
            return false;
        }

        return parameter.GetGenericParameterConstraints()
            .All(constraint => constraint.ContainsGenericParameters || constraint.IsAssignableFrom(candidate));
    }

    // Emits the call, so by-reference and by-ref-like parameters (spans, say) receive default values as well.
    private static void InvokeWithDefaultArguments(MethodBase method)
    {
        Type declaring = method.DeclaringType!;
        var invoker = new DynamicMethod(
            "Invoke" + method.Name,
            typeof(void),
            [typeof(object)],
            typeof(ThreadAffinityConformanceTests).Module,
            skipVisibility: true);
        ILGenerator il = invoker.GetILGenerator();
        if (method is MethodInfo { IsStatic: false })
        {
            if (declaring.IsValueType)
            {
                EmitDefault(il, declaring, byReference: true);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Castclass, declaring);
            }
        }

        foreach (ParameterInfo parameter in method.GetParameters())
        {
            Type type = parameter.ParameterType;
            EmitDefault(il, type.IsByRef ? type.GetElementType()! : type, type.IsByRef);
        }

        if (method is ConstructorInfo constructor)
        {
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Pop);
        }
        else
        {
            var target = (MethodInfo)method;
            il.Emit(OpCodes.Call, target);
            if (target.ReturnType != typeof(void))
            {
                il.Emit(OpCodes.Pop);
            }
        }

        il.Emit(OpCodes.Ret);
        object? receiver = method is MethodInfo { IsStatic: false } && !declaring.IsValueType
            ? RuntimeHelpers.GetUninitializedObject(declaring)
            : null;
        invoker.CreateDelegate<Action<object?>>()(receiver);
    }

    private static void EmitDefault(ILGenerator il, Type type, bool byReference)
    {
        LocalBuilder local = il.DeclareLocal(type);
        il.Emit(OpCodes.Ldloca, local);
        il.Emit(OpCodes.Initobj, type);
        il.Emit(byReference ? OpCodes.Ldloca : OpCodes.Ldloc, local);
    }

    private struct StateMachineStub : IAsyncStateMachine
    {
        public readonly void MoveNext()
        {
        }

        public readonly void SetStateMachine(IAsyncStateMachine stateMachine)
        {
        }
    }

    private readonly struct AwaiterStub : ICriticalNotifyCompletion
    {
        public void OnCompleted(Action continuation)
        {
        }

        public void UnsafeOnCompleted(Action continuation)
        {
        }
    }
}
