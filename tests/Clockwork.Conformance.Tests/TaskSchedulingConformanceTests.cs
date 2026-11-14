using System.Reflection;
using System.Threading.Tasks;
using Clockwork.Runtime;
using Clockwork.Runtime.Execution;

namespace Clockwork.Conformance.Tests;

/// <summary>
/// End-to-end conformance for the tasks a probe constructs with <c>new Task(...)</c> and for the one scheduler policy of
/// every controlled API which takes a <see cref="TaskScheduler"/>. A started task runs as its own strand, so it owns its
/// own locks rather than its creator's, as a task on its own thread does; a task run inline shares its caller's.
/// <see cref="TaskCreationOptions.AttachedToParent"/> is rejected, so a parent run synchronously never waits for a child.
/// <see cref="TaskScheduler.Default"/>, the coordinator's scheduler, and a node's <see cref="SimulationTaskScheduler"/> are
/// accepted at every entry point, the last running the work on its node. <c>ValueTask&lt;TResult&gt;.Result</c> pumps the
/// coordinator. Each outcome the BCL can produce deterministically is compared with an uninstrumented run.
/// </summary>
public sealed class TaskSchedulingConformanceTests : IDisposable
{
    private const string ProbeType = "Conf.TaskSchedulingProbe";

    private static readonly DateTimeOffset Start = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Source = """
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Conf
        {
            public static class TaskSchedulingProbe
            {
                // The creator holds a lock while the task it started takes it: the task waits until the creator leaves.
                public static string StartedTaskWaitsForItsCreatorsLock()
                {
                    var gate = new object();
                    var log = new List<string>();
                    Task started;
                    lock (gate)
                    {
                        started = new Task(() =>
                        {
                            lock (gate)
                            {
                                log.Add("started entered");
                            }
                        });
                        started.Start();
                        Thread.Sleep(10);
                        log.Add("creator leaving");
                    }

                    started.Wait();
                    return string.Join("|", log);
                }

                // A started task does not own the lock its creator holds, so it cannot release it.
                public static string StartedTaskCannotReleaseItsCreatorsLock()
                {
                    var gate = new object();
                    Monitor.Enter(gate);
                    try
                    {
                        var started = new Task(() => Monitor.Exit(gate));
                        started.Start();

                        // Polled rather than waited for, which would let the BCL run the task inline on this thread.
                        while (!started.IsCompleted)
                        {
                            Thread.Sleep(1);
                        }

                        return started.Exception?.InnerException.GetType().Name ?? "released";
                    }
                    finally
                    {
                        if (Monitor.IsEntered(gate))
                        {
                            Monitor.Exit(gate);
                        }
                    }
                }

                // A task run inline runs as its caller, which may re-enter the lock the caller holds.
                public static string InlineTaskSharesItsCallersLocks()
                {
                    var gate = new object();
                    lock (gate)
                    {
                        var inline = new Task<bool>(() =>
                        {
                            lock (gate)
                            {
                                return Monitor.IsEntered(gate);
                            }
                        });
                        inline.RunSynchronously();
                        return inline.Status + ":" + inline.Result;
                    }
                }

                public static string ConstructorsKeepTheBclsShape()
                {
                    var log = new List<string>();
                    log.Add(Describe(() => new Task((Action)null)));
                    log.Add(Describe(() => new Task<int>((Func<int>)null)));
                    log.Add(Describe(() => new Task((Action<object>)null, "state")));
                    log.Add(Describe(() => new Task<int>((Func<object, int>)null, "state")));
                    log.Add(Describe(() => new Task(() => { }, (TaskCreationOptions)0x10000)));
                    log.Add(new Task(state => { }, "state").AsyncState as string);
                    log.Add(new Task<int>(state => 1, "generic state").AsyncState as string);
                    log.Add(new Task(() => { }, TaskCreationOptions.LongRunning).CreationOptions.ToString());
                    log.Add(new Task<int>(() => 1, new CancellationToken(true)).Status.ToString());

                    var withState = new Task<string>(state => "ran:" + state, "s", CancellationToken.None, TaskCreationOptions.DenyChildAttach);
                    withState.Start();
                    log.Add(withState.Result);

                    var canceled = new Task(() => { }, new CancellationToken(true));
                    log.Add("start canceled:" + Describe(() =>
                    {
                        canceled.Start();
                        return "started";
                    }));

                    return string.Join("|", log);
                }

                public static void AttachedChild() => new Task(() => { }, TaskCreationOptions.AttachedToParent);

                public static void AttachedGenericChild() =>
                    new Task<int>(() => 1, CancellationToken.None, TaskCreationOptions.AttachedToParent | TaskCreationOptions.LongRunning);

                // A parent which creates an attached child faults, rather than waiting for the child forever.
                public static string RunSynchronouslyWithAnAttachedChild()
                {
                    var parent = new Task(() => new Task(() => { }, TaskCreationOptions.AttachedToParent).Start());
                    parent.RunSynchronously();
                    return parent.Status + ":" + parent.Exception.InnerException.GetType().Name;
                }

                // A promise asks to attach to the running task; the constructed task denies attachment, so
                // RunSynchronously returns once the body does instead of waiting for the promise on the logical thread.
                public static string RunSynchronouslyWithAnAttachedPromise()
                {
                    var parent = new Task(() =>
                    {
                        var promise = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
                        Task.Run(() => promise.SetResult());
                    });
                    parent.RunSynchronously();
                    return parent.Status.ToString();
                }
                public static async Task<string> SchedulersRunWorkOnTheirNode(TaskScheduler otherNode, Func<string> node)
                {
                    var log = new List<string>();
                    log.Add("startnew default:" + await Task.Factory.StartNew(node, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default));
                    log.Add("startnew other:" + await Task.Factory.StartNew(node, CancellationToken.None, TaskCreationOptions.None, otherNode));
                    log.Add("continuewith other:" + await Task.CompletedTask.ContinueWith(_ => node(), otherNode));
                    log.Add("continuewhenall other:" + await Task.Factory.ContinueWhenAll(new[] { Task.CompletedTask }, _ => node(), CancellationToken.None, TaskContinuationOptions.None, otherNode));

                    var started = new Task<string>(node);
                    started.Start(otherNode);
                    log.Add("start other:" + await started);

                    var inline = new Task<string>(node);
                    inline.RunSynchronously(otherNode);
                    log.Add("runsynchronously other:" + inline.Status + ":" + inline.Result);

                    var current = new Task<string>(() =>
                    {
                        string viaFactory = Task.Factory.StartNew(node, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Current).Result;
                        var viaStart = new Task<string>(node);
                        viaStart.Start(TaskScheduler.Current);
                        string viaContinuation = Task.CompletedTask.ContinueWith(_ => node(), TaskScheduler.Current).Result;
                        return viaFactory + "," + viaStart.Result + "," + viaContinuation;
                    });
                    current.Start();
                    log.Add("current inside a started task:" + await current);

                    var seen = new ConcurrentBag<string>();
                    await Parallel.ForEachAsync(
                        new[] { 1, 2, 3 },
                        new ParallelOptions { TaskScheduler = otherNode },
                        (item, cancellationToken) =>
                        {
                            seen.Add(node());
                            return default;
                        });
                    log.Add("parallel other:" + string.Join(",", seen.Distinct()));
                    Parallel.For(0, 3, new ParallelOptions { TaskScheduler = TaskScheduler.Default }, i => seen.Add("default"));
                    log.Add("parallel default:" + seen.Count);
                    return string.Join("|", log);
                }

                // A framework operator awaits a task-backed source with ConfigureAwait(false); the source's task completes
                // inside a started task and inside a task run inline, whose scheduler the BCL must see as the default for
                // it to run the operator's continuation inline rather than on the thread pool.
                public static async Task<string> FrameworkContinuationsCompletedInsideConstructedTasks(Func<bool> onSimulationThread)
                {
                    var observed = new List<string>();
                    var source = new TaskBackedSequence();
                    Task<List<int>> list = source
                        .Append(9)
                        .Select(item =>
                        {
                            observed.Add(item + ":" + onSimulationThread());
                            return item;
                        })
                        .ToListAsync()
                        .AsTask();
                    await Task.Yield();

                    var started = new Task(() => source.Produce(1));
                    started.Start();
                    await started;
                    await Task.Delay(1);
                    var inline = new Task(() => source.Produce(2));
                    inline.RunSynchronously();
                    await Task.Delay(1);
                    var finished = new Task(() => source.Finish());
                    finished.Start();
                    await finished;

                    List<int> items = await list;
                    return string.Join(",", observed) + "|" + string.Join(",", items);
                }

                // A framework operator awaits a task-backed source with ConfigureAwait(false), and the caller's 'run' runs
                // the code which completes the source's task: inline, or in a task which uninstrumented code constructs.
                // The BCL must see wherever that code runs as a thread-pool thread for it to run the operator's
                // continuation inline rather than on the thread pool.
                public static async Task<string> FrameworkContinuationsCompletedWhereTheCallerRuns(
                    Func<Action, Task> run,
                    Func<bool> onSimulationThread)
                {
                    var observed = new List<string>();
                    var source = new TaskBackedSequence();
                    Task<List<int>> list = source
                        .Append(9)
                        .Select(item =>
                        {
                            observed.Add(item + ":" + onSimulationThread());
                            return item;
                        })
                        .ToListAsync()
                        .AsTask();

                    Task completing = run(() =>
                    {
                        source.Produce(1);
                        source.Produce(2);
                        source.Finish();
                    });
                    if (completing.Status == TaskStatus.Created)
                    {
                        completing.Start();
                    }

                    await completing;
                    List<int> items = await list;
                    return string.Join(",", observed) + "|" + string.Join(",", items);
                }

                // Buffers what it produces, so a consumer which continues later, on a context it captured, sees every item.
                public sealed class TaskBackedSequence : IAsyncEnumerable<int>
                {
                    private readonly Queue<int> _items = new Queue<int>();
                    private TaskCompletionSource<bool> _waiting;
                    private bool _finished;
                    private int _current;

                    public void Produce(int item)
                    {
                        _items.Enqueue(item);
                        Deliver();
                    }

                    public void Finish()
                    {
                        _finished = true;
                        Deliver();
                    }

                    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(this);

                    private void Deliver()
                    {
                        TaskCompletionSource<bool> waiting = _waiting;
                        if (waiting == null || (_items.Count == 0 && !_finished))
                        {
                            return;
                        }

                        _waiting = null;
                        waiting.SetResult(TryTake());
                    }

                    private bool TryTake()
                    {
                        if (_items.Count == 0)
                        {
                            return false;
                        }

                        _current = _items.Dequeue();
                        return true;
                    }

                    private ValueTask<bool> MoveNextAsync()
                    {
                        if (_items.Count > 0 || _finished)
                        {
                            return new ValueTask<bool>(TryTake());
                        }

                        _waiting = new TaskCompletionSource<bool>();
                        return new ValueTask<bool>(_waiting.Task);
                    }

                    private sealed class Enumerator : IAsyncEnumerator<int>
                    {
                        private readonly TaskBackedSequence _owner;

                        public Enumerator(TaskBackedSequence owner) => _owner = owner;

                        public int Current => _owner._current;

                        public ValueTask<bool> MoveNextAsync() => _owner.MoveNextAsync();

                        public ValueTask DisposeAsync() => default;
                    }
                }

                public static async Task<string> StartAndAwait(Task<string> task)
                {
                    task.Start();
                    return await task;
                }

                // Runs inside a task constructed by uninstrumented code, whose scheduler is visible: TaskScheduler.Current
                // is the coordinator's, which every controlled API taking a scheduler accepts.
                public static string ScheduleOnTheCurrentScheduler(Func<string> node)
                {
                    bool visible = TaskScheduler.Current != TaskScheduler.Default;
                    string viaFactory = Task.Factory.StartNew(node, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Current).Result;
                    string viaContinuation = Task.CompletedTask.ContinueWith(_ => node(), TaskScheduler.Current).Result;
                    return "visible:" + visible + "|startnew:" + viaFactory + "|continuewith:" + viaContinuation;
                }
                public static void RunSynchronouslyOnCustomScheduler() =>
                    new Task(() => { }).RunSynchronously(new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

                public static void StartNewOnCustomScheduler() =>
                    Task.Factory.StartNew(() => { }, CancellationToken.None, TaskCreationOptions.None, new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler);

                // A value task which another operation completes later: reading its Result pumps the coordinator.
                public static string ValueTaskResultWaitsForPendingWork()
                {
                    var source = new TaskCompletionSource<int>();
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10);
                        source.SetResult(5);
                    });
                    return new ValueTask<int>(source.Task).Result.ToString();
                }

                public static string ValueTaskResultKeepsTheBclsOutcomes()
                {
                    var log = new List<string>();
                    log.Add(new ValueTask<int>(3).Result.ToString());
                    log.Add(new ValueTask<int>(Task.FromResult(4)).Result.ToString());
                    log.Add(Describe(() => new ValueTask<int>(Task.FromException<int>(new InvalidOperationException("boom"))).Result));
                    log.Add(Describe(() => new ValueTask<int>(Task.FromCanceled<int>(new CancellationToken(true))).Result));
                    return string.Join("|", log);
                }

                private static string Describe(Func<object> create)
                {
                    try
                    {
                        return "value:" + create();
                    }
                    catch (ArgumentException exception)
                    {
                        return exception.GetType().Name + ":" + exception.ParamName;
                    }
                    catch (Exception exception)
                    {
                        return exception.GetType().Name;
                    }
                }
            }
        }
        """;

    private readonly RewriteFixture _fixture = new();
    private readonly Lazy<StagedProbe> _release;
    private readonly Lazy<StagedProbe> _debug;
    private readonly Lazy<UninstrumentedProbe> _bcl;

    public TaskSchedulingConformanceTests()
    {
        _release = new(() => _fixture.StageControlledTasks("Conf.TaskSchedulingRel", ProbeType, Source, optimize: true));
        _debug = new(() => _fixture.StageControlledTasks("Conf.TaskSchedulingDbg", ProbeType, Source, optimize: false));
        _bcl = new(() => _fixture.CompileUninstrumented("Conf.TaskSchedulingBcl", ProbeType, Source));
    }

    [Theory]
    [InlineData("StartedTaskWaitsForItsCreatorsLock", "creator leaving|started entered")]
    [InlineData("StartedTaskCannotReleaseItsCreatorsLock", "SynchronizationLockException")]
    [InlineData("InlineTaskSharesItsCallersLocks", "RanToCompletion:True")]
    public void ATaskOwnsTheLocksOfTheStrandWhichRunsIt(string probe, string expected)
    {
        foreach (bool optimize in new[] { true, false })
        {
            using var host = new SimulationHost(Start);

            Assert.Equal(expected, (string)host.Invoke(Method(probe, optimize))!);
        }

        Assert.Equal(expected, (string)_bcl.Value.Method(probe).Invoke(null, null)!);
    }

    [Fact]
    public void ConstructedTasksKeepTheBclsValidationStateAndOutcomes()
    {
        using var host = new SimulationHost(Start);

        var controlled = (string)host.Invoke(Method("ConstructorsKeepTheBclsShape"))!;
        var bcl = (string)_bcl.Value.Method("ConstructorsKeepTheBclsShape").Invoke(null, null)!;

        Assert.Equal(
            "ArgumentNullException:action|ArgumentNullException:action|ArgumentNullException:action|" +
            "ArgumentNullException:action|ArgumentOutOfRangeException:creationOptions|state|generic state|LongRunning|" +
            "Canceled|ran:s|start canceled:InvalidOperationException",
            controlled);
        Assert.Equal(bcl, controlled);
    }

    [Theory]
    [InlineData("AttachedChild", "System.Threading.Tasks.Task..ctor")]
    [InlineData("AttachedGenericChild", "System.Threading.Tasks.Task`1..ctor")]
    public void AttachedToParentIsRejected(string probe, string apiName)
    {
        using var host = new SimulationHost(Start);

        var exception = Assert.IsType<SimulationApiException>(Record.Exception(() => host.Invoke(Method(probe))));

        Assert.Equal(apiName, exception.ApiName);
    }

    [Fact]
    public void RunSynchronouslyOfAParentWithAnAttachedChildFaultsInsteadOfHanging()
    {
        using var host = new SimulationHost(Start);

        Assert.Equal("Faulted:SimulationApiException", (string)host.Invoke(Method("RunSynchronouslyWithAnAttachedChild"))!);
    }

    [Fact]
    public void RunSynchronouslyOfAConstructedTaskDoesNotWaitForAPromiseAskingToAttach()
    {
        using var host = new SimulationHost(Start);

        var controlled = (string)host.Invoke(Method("RunSynchronouslyWithAnAttachedPromise"))!;
        var bcl = (string)_bcl.Value.Method("RunSynchronouslyWithAnAttachedPromise").Invoke(null, null)!;

        Assert.Equal("RanToCompletion", controlled);
        Assert.Equal(bcl, controlled);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EverySchedulerEntryPointRunsWorkOnTheSchedulersNode(bool optimize)
    {
        using var host = new SimulationHost(Start, nodeAddresses: ["a", "b"]);
        Func<string> node = () => SimulationExecutionContext.Current?.Node?.Address ?? "none";

        string log = Completed<string>(host.Invoke(Method("SchedulersRunWorkOnTheirNode", optimize), host.NodeTaskScheduler("b"), node));

        Assert.Equal(
            "startnew default:a|startnew other:b|continuewith other:b|continuewhenall other:b|start other:b|" +
            "runsynchronously other:RanToCompletion:b|current inside a started task:a,a,a|parallel other:b|parallel default:6",
            log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FrameworkContinuationsWhichAConstructedTaskCompletesRunOnTheLogicalThread(bool optimize)
    {
        using var host = new SimulationHost(Start);

        string log = Completed<string>(host.Invoke(
            Method("FrameworkContinuationsCompletedInsideConstructedTasks", optimize),
            new Func<bool>(() => host.Scheduler.IsSimulationThread)));

        string bcl = await ((Task<string>)_bcl.Value.Method("FrameworkContinuationsCompletedInsideConstructedTasks")
            .Invoke(null, [new Func<bool>(() => true)])!).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(bcl, log);
        Assert.Equal("1:True,2:True,9:True|1,2,9", log);
    }

    public static TheoryData<string, bool> CompletionLocations
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (string location in new[] { "root", "lane", "lane task", "uninstrumented task" })
            {
                foreach (bool optimize in new[] { true, false })
                {
                    data.Add(location, optimize);
                }
            }

            return data;
        }
    }

    // Rewritten code completes a framework operator's task-backed source where Clockwork's cooperative surfaces are
    // current: before the first await of RunToCompletion's root task, which runs visibly on the cluster's
    // SimulationTaskScheduler with its SimulationSynchronizationContext; in a scheduler lane's work, which runs with the
    // lane's context; in a task which uninstrumented code starts on a node's SimulationTaskScheduler; and in a task which
    // uninstrumented code constructs and rewritten code starts, which runs visibly on the coordinator's scheduler.
    [Theory]
    [MemberData(nameof(CompletionLocations))]
    public async Task FrameworkContinuationsWhichRewrittenCodeCompletesRunOnTheLogicalThread(string location, bool optimize)
    {
        using var host = new SimulationHost(Start);
        MethodInfo probe = Method("FrameworkContinuationsCompletedWhereTheCallerRuns", optimize);
        Func<bool> onSimulationThread = () => host.Scheduler.IsSimulationThread;
        SimulationTaskScheduler lane = host.NodeTaskScheduler("node");

        string log = location switch
        {
            "root" => host.RunToCompletion<string>(probe, RunInline, onSimulationThread),
            "lane" => Completed<string>(host.Invoke(probe, RunInline, onSimulationThread)),
            "lane task" => Completed<string>(host.Invoke(
                probe,
                new Func<Action, Task>(body => Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.None, lane)),
                onSimulationThread)),
            _ => Completed<string>(host.Invoke(probe, new Func<Action, Task>(body => new Task(body)), onSimulationThread)),
        };

        string bcl = await ((Task<string>)_bcl.Value.Method("FrameworkContinuationsCompletedWhereTheCallerRuns")
            .Invoke(null, [RunInline, new Func<bool>(() => true)])!).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(bcl, log);
        Assert.Equal("1:True,2:True,9:True|1,2,9", log);
    }
    [Fact]
    public void ATaskWhichUninstrumentedCodeConstructsRunsOnTheCoordinatorsScheduler()
    {
        using var host = new SimulationHost(Start);
        Func<string> node = () => SimulationExecutionContext.Current?.Node?.Address ?? "none";
        MethodInfo schedule = Method("ScheduleOnTheCurrentScheduler");

        // The test assembly is not rewritten, so this constructs the task with the BCL's constructor, which hides no
        // scheduler, inside the simulation, which its captured context carries.
        var task = (Task<string>)host.Invoke(
            typeof(TaskSchedulingConformanceTests).GetMethod(nameof(ConstructUninstrumented), BindingFlags.NonPublic | BindingFlags.Static)!,
            schedule,
            node)!;
        string log = Completed<string>(host.Invoke(Method("StartAndAwait"), task));

        Assert.Equal("visible:True|startnew:node|continuewith:node", log);
    }
    [Theory]
    [InlineData("RunSynchronouslyOnCustomScheduler", "System.Threading.Tasks.Task.RunSynchronously")]
    [InlineData("StartNewOnCustomScheduler", "System.Threading.Tasks.TaskFactory.StartNew")]
    public void ACustomSchedulerIsRejected(string probe, string apiName)
    {
        using var host = new SimulationHost(Start);

        var exception = Assert.IsType<SimulationApiException>(Record.Exception(() => host.Invoke(Method(probe))));

        Assert.Equal(apiName, exception.ApiName);
    }

    [Fact]
    public void ValueTaskResultPumpsTheCoordinatorUntilTheValueTaskCompletes()
    {
        using var host = new SimulationHost(Start);

        Assert.Equal("5", (string)host.Invoke(Method("ValueTaskResultWaitsForPendingWork"))!);
    }

    [Fact]
    public void ValueTaskResultKeepsTheBclsOutcomes()
    {
        using var host = new SimulationHost(Start);

        var controlled = (string)host.Invoke(Method("ValueTaskResultKeepsTheBclsOutcomes"))!;
        var bcl = (string)_bcl.Value.Method("ValueTaskResultKeepsTheBclsOutcomes").Invoke(null, null)!;

        Assert.Equal("3|4|InvalidOperationException|TaskCanceledException", controlled);
        Assert.Equal(bcl, controlled);
    }

    [Theory]
    [InlineData("StartedTaskWaitsForItsCreatorsLock", "clockwork.tasks.ctor.action", "started entered|creator leaving")]
    [InlineData("StartedTaskCannotReleaseItsCreatorsLock", "clockwork.tasks.ctor.action", "released")]
    public void WithoutTheConstructorRulesAStartedTaskRunsAsItsCreator(string probe, string ruleId, string escaped)
    {
        StagedProbe staged = _fixture.StageControlledTasksWithout(
            "Conf.TaskSchedulingEscape." + probe,
            ProbeType,
            Source,
            rule => rule.Id == ruleId);
        using var host = new SimulationHost(Start);

        Assert.Equal(escaped, (string)host.Invoke(staged.Method(probe))!);
    }

    private static Task<string> ConstructUninstrumented(MethodInfo schedule, Func<string> node) =>
        new(() => (string)schedule.Invoke(null, [node])!);

    private static readonly Func<Action, Task> RunInline = body =>
    {
        body();
        return Task.CompletedTask;
    };
    private static T Completed<T>(object? task)
    {
        var typed = Assert.IsAssignableFrom<Task<T>>(task);
        Assert.Equal(TaskStatus.RanToCompletion, typed.Status);
        return typed.GetAwaiter().GetResult();
    }

    private MethodInfo Method(string name, bool optimize = true) => (optimize ? _release : _debug).Value.Method(name);

    public void Dispose() => _fixture.Dispose();
}
