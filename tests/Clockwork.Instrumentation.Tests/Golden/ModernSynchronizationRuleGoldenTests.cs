using System.Collections.Immutable;
using Clockwork.Instrumentation.Manifest;
using Clockwork.Instrumentation.Rewriting;
using Clockwork.Instrumentation.Rules.BuiltIn;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Clockwork.Runtime.Policy;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Golden;

/// <summary>
/// Exhaustive end-to-end coverage for the Phase 8A synchronization inventory. The direct fixture invokes
/// every receiver-first or factory rule, while the closure fixture proves whole-type substitutions also
/// retarget compiler-generated captures and <c>Action&lt;Barrier&gt;</c>.
/// </summary>
public sealed class ModernSynchronizationRuleGoldenTests
{
    private const string DirectFixture = """
        using System;
        using System.Runtime.Serialization;
        using System.Threading;

        namespace Fx
        {
            public static class ModernSynchronizationUser
            {
                public static ReaderWriterLockSlim ReaderWriterDefault() => new ReaderWriterLockSlim();
                public static ReaderWriterLockSlim ReaderWriterRecursive() => new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
                public static int ReaderWriterProperties(ReaderWriterLockSlim value) =>
                    value.CurrentReadCount + value.RecursiveReadCount + value.RecursiveUpgradeCount + value.RecursiveWriteCount +
                    value.WaitingReadCount + value.WaitingUpgradeCount + value.WaitingWriteCount +
                    (value.IsReadLockHeld ? 1 : 0) + (value.IsUpgradeableReadLockHeld ? 1 : 0) +
                    (value.IsWriteLockHeld ? 1 : 0) + (int)value.RecursionPolicy;
                public static void ReaderWriterMembers(ReaderWriterLockSlim value)
                {
                    value.EnterReadLock(); _ = value.TryEnterReadLock(0); _ = value.TryEnterReadLock(TimeSpan.Zero); value.ExitReadLock();
                    value.EnterUpgradeableReadLock(); _ = value.TryEnterUpgradeableReadLock(0); _ = value.TryEnterUpgradeableReadLock(TimeSpan.Zero); value.ExitUpgradeableReadLock();
                    value.EnterWriteLock(); _ = value.TryEnterWriteLock(0); _ = value.TryEnterWriteLock(TimeSpan.Zero); value.ExitWriteLock();
                    value.Dispose();
                }

                public static ManualResetEventSlim EventDefault() => new ManualResetEventSlim();
                public static ManualResetEventSlim EventInitial() => new ManualResetEventSlim(true);
                public static ManualResetEventSlim EventSpin() => new ManualResetEventSlim(false, 1);
                public static int EventProperties(ManualResetEventSlim value) => (value.IsSet ? 1 : 0) + value.SpinCount + (value.WaitHandle is null ? 0 : 1);
                public static void EventMembers(ManualResetEventSlim value, CancellationToken token)
                {
                    value.Set(); value.Reset(); value.Wait(); value.Wait(token);
                    _ = value.Wait(0); _ = value.Wait(0, token); _ = value.Wait(TimeSpan.Zero); _ = value.Wait(TimeSpan.Zero, token);
                    value.Dispose();
                }

                public static Mutex MutexDefault() => new Mutex();
                public static Mutex MutexInitial() => new Mutex(false);
                public static void MutexNamed()
                {
                    _ = new Mutex(false, "named");
                    _ = new Mutex(false, "named", out bool first);
                    _ = new Mutex(false, "named", default(NamedWaitHandleOptions));
                    _ = new Mutex(false, "named", default(NamedWaitHandleOptions), out bool second);
                    _ = new Mutex("named", default(NamedWaitHandleOptions));
                    _ = Mutex.OpenExisting("named");
                    _ = Mutex.OpenExisting("named", default(NamedWaitHandleOptions));
                    _ = Mutex.TryOpenExisting("named", out Mutex opened);
                    _ = Mutex.TryOpenExisting("named", default(NamedWaitHandleOptions), out Mutex openedWithOptions);
                }
                public static void MutexRelease(Mutex value) => value.ReleaseMutex();

                public static Semaphore SemaphoreDefault() => new Semaphore(1, 2);
                public static void SemaphoreNamed()
                {
                    _ = new Semaphore(1, 2, "named");
                    _ = new Semaphore(1, 2, "named", out bool first);
                    _ = new Semaphore(1, 2, "named", default(NamedWaitHandleOptions));
                    _ = new Semaphore(1, 2, "named", default(NamedWaitHandleOptions), out bool second);
                    _ = Semaphore.OpenExisting("named");
                    _ = Semaphore.OpenExisting("named", default(NamedWaitHandleOptions));
                    _ = Semaphore.TryOpenExisting("named", out Semaphore opened);
                    _ = Semaphore.TryOpenExisting("named", default(NamedWaitHandleOptions), out Semaphore openedWithOptions);
                }
                public static int SemaphoreRelease(Semaphore value) => value.Release() + value.Release(1);

                public static void ExecutionContextMembers(ExecutionContext value, SerializationInfo info, StreamingContext streaming)
                {
                    _ = ExecutionContext.Capture();
                    ExecutionContext.Run(value, _ => { }, null);
                    _ = ExecutionContext.SuppressFlow();
                    ExecutionContext.RestoreFlow();
                    _ = ExecutionContext.IsFlowSuppressed();
                    ExecutionContext.Restore(value);
                    _ = value.CreateCopy();
                    value.Dispose();
                    value.GetObjectData(info, streaming);
                }

                public static void SynchronizationContextMembers(SynchronizationContext value)
                {
                    _ = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(value);
                    _ = value.CreateCopy();
                    _ = value.IsWaitNotificationRequired();
                    value.OperationStarted();
                    value.OperationCompleted();
                    value.Post(_ => { }, null);
                    value.Send(_ => { }, null);
                    _ = value.Wait(Array.Empty<IntPtr>(), false, 0);
                }

                public static void SpinLockMembers(ref SpinLock value)
                {
                    _ = value.IsHeld; _ = value.IsHeldByCurrentThread; _ = value.IsThreadOwnerTrackingEnabled;
                    bool taken = false; value.Enter(ref taken); value.Exit(); value.Exit(false);
                    taken = false; value.TryEnter(ref taken); taken = false; value.TryEnter(0, ref taken);
                    taken = false; value.TryEnter(TimeSpan.Zero, ref taken);
                }
                public static SpinLock SpinLockConstructor() => new SpinLock(true);

                public static Barrier BarrierDefault() => new Barrier(1);
                public static Barrier BarrierCallback() => new Barrier(1, barrier => BarrierCallbackBody(barrier));
                public static void BarrierCallbackBody(Barrier barrier) => _ = barrier.CurrentPhaseNumber;
                public static void BarrierMembers(Barrier value, CancellationToken token)
                {
                    _ = value.CurrentPhaseNumber; _ = value.ParticipantCount; _ = value.ParticipantsRemaining;
                    _ = value.AddParticipant(); _ = value.AddParticipants(1); value.RemoveParticipant(); value.RemoveParticipants(1);
                    value.SignalAndWait(); value.SignalAndWait(token); _ = value.SignalAndWait(0); _ = value.SignalAndWait(0, token);
                    _ = value.SignalAndWait(TimeSpan.Zero); _ = value.SignalAndWait(TimeSpan.Zero, token); value.Dispose();
                }

                public static CountdownEvent CountdownDefault() => new CountdownEvent(1);
                public static int CountdownProperties(CountdownEvent value) =>
                    value.CurrentCount + value.InitialCount + (value.IsSet ? 1 : 0) + (value.WaitHandle is null ? 0 : 1);
                public static void CountdownMembers(CountdownEvent value, CancellationToken token)
                {
                    value.AddCount(); value.AddCount(1); _ = value.TryAddCount(); _ = value.TryAddCount(1);
                    _ = value.Signal(); _ = value.Signal(1); value.Reset(); value.Reset(1);
                    value.Wait(); value.Wait(token); _ = value.Wait(0); _ = value.Wait(0, token);
                    _ = value.Wait(TimeSpan.Zero); _ = value.Wait(TimeSpan.Zero, token); value.Dispose();
                }
            }
        }
        """;

    private const string ClosureFixture = """
        using System;
        using System.Threading;

        namespace Fx
        {
            public static class Captures
            {
                public static Action Capture(Barrier barrier, CountdownEvent countdown) =>
                    () => { _ = barrier.CurrentPhaseNumber; countdown.Signal(); };

                public static Barrier CreateWithCallback() =>
                    new Barrier(1, barrier => { _ = barrier.ParticipantCount; });
            }
        }
        """;

    private static string RuntimeAssemblyPath =>
        typeof(Clockwork.Runtime.Threading.ControlledBarrier).Assembly.Location;

    [Fact]
    public void EveryPhase8ADirectRuleRewritesToTheControlledRuntimeAndManifest()
    {
        using var context = RewriteTestContext.Create();
        RewriteResult result = Rewrite(context, "Fx.ModernSynchronization", DirectFixture);

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ModernSynchronization.rewritten.dll"));

        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledReaderWriterLockSlim"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledManualResetEventSlim"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledMutex"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledSemaphore"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledExecutionContext"));
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, "ControlledSynchronizationContext"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "ReaderWriterLockSlim::.ctor"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "ManualResetEventSlim::.ctor"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "Threading.Mutex::.ctor"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "Threading.Semaphore::.ctor"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "System.Threading.ExecutionContext::Capture"));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, "System.Threading.SynchronizationContext::Wait"));

        AssertWholeTypeWasRetargeted(module, "System.Threading.SpinLock", "Clockwork.Runtime.Threading.ControlledSpinLock");
        AssertWholeTypeWasRetargeted(module, "System.Threading.Barrier", "Clockwork.Runtime.Threading.ControlledBarrier");
        AssertWholeTypeWasRetargeted(module, "System.Threading.CountdownEvent", "Clockwork.Runtime.Threading.ControlledCountdownEvent");

        BuiltInRuleFamily[] phase8AFamilies =
        [
            BuiltInRuleFamily.ReaderWriterLockSlim,
            BuiltInRuleFamily.ManualResetEventSlim,
            BuiltInRuleFamily.Mutex,
            BuiltInRuleFamily.KernelSemaphore,
            BuiltInRuleFamily.SpinLock,
            BuiltInRuleFamily.ExecutionContext,
            BuiltInRuleFamily.SynchronizationContext,
            BuiltInRuleFamily.Barrier,
            BuiltInRuleFamily.CountdownEvent,
        ];
        ImmutableHashSet<string> transformed = result.Manifest.Transformations
            .Select(transformation => transformation.RuleId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        foreach ((BuiltInRuleFamily _, Clockwork.Instrumentation.Rules.RewriteRule rule) in BuiltInRuleSets.ControlledTasksInventory
            .Where(entry => phase8AFamilies.Contains(entry.Family)))
        {
            Assert.Contains(rule.Id, transformed);
            if (rule.Target.MemberName is not null)
            {
                Assert.False(CecilInspect.AnyMethodCallsContaining(
                    module,
                    rule.Target.DeclaringTypeFullName + "::" + rule.Target.MemberName),
                    $"The original target for '{rule.Id}' remained in the rewritten fixture.");
            }
        }

        Assert.Contains(result.Manifest.Transformations, transformation =>
            transformation.RuleId == "clockwork.mutex.openexisting" &&
            transformation.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(result.Manifest.Transformations, transformation =>
            transformation.RuleId == "clockwork.executioncontext.getobjectdata" &&
            transformation.Policy == SimulationApiPolicy.Rejected);
        Assert.Contains(result.Manifest.Transformations, transformation =>
            transformation.RuleId == "clockwork.synchronizationcontext.wait" &&
            transformation.Policy == SimulationApiPolicy.Rejected);
    }

    [Fact]
    public void WholeTypeSubstitutionsRetargetClosureFieldsAndBarrierCallbackGenericArguments()
    {
        using var context = RewriteTestContext.Create();
        Rewrite(context, "Fx.ModernSynchronizationClosures", ClosureFixture);

        using ModuleDefinition module = context.LoadModule(
            Path.Combine(context.Directory, "Fx.ModernSynchronizationClosures.rewritten.dll"));
        AssertWholeTypeWasRetargeted(module, "System.Threading.Barrier", "Clockwork.Runtime.Threading.ControlledBarrier");
        AssertWholeTypeWasRetargeted(module, "System.Threading.CountdownEvent", "Clockwork.Runtime.Threading.ControlledCountdownEvent");
        Assert.DoesNotContain(
            module.GetTypes().SelectMany(type => type.Fields),
            field => field.FieldType.FullName.Contains("System.Threading.Barrier", StringComparison.Ordinal)
                || field.FieldType.FullName.Contains("System.Threading.CountdownEvent", StringComparison.Ordinal));
    }

    private static RewriteResult Rewrite(RewriteTestContext context, string assemblyName, string fixture)
    {
        string fixturePath = context.CompileFixture(assemblyName, fixture);
        string outputPath = Path.Combine(context.Directory, assemblyName + ".rewritten.dll");
        var options = new RewriteOptions
        {
            ReplacementAssemblyPaths = [RuntimeAssemblyPath],
            ReferenceSearchDirectories = [context.Directory, Path.GetDirectoryName(RuntimeAssemblyPath)!],
        };

        RewriteResult result = RewriteEngine.Rewrite(new RewriteRequest(
            fixturePath,
            outputPath,
            BuiltInRuleSets.BuildControlledTasks(BuiltInRuleSets.AllFamilies),
            options));
        result.EnsureSuccess();
        return result;
    }

    private static void AssertWholeTypeWasRetargeted(ModuleDefinition module, string original, string controlled)
    {
        Assert.True(CecilInspect.AnyMethodCallsContaining(module, controlled));
        Assert.False(CecilInspect.AnyMethodCallsContaining(module, original));
    }
}
