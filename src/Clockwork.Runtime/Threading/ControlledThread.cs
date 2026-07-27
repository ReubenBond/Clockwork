using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Clockwork.Runtime.Shims;
using Clockwork.Runtime.Tasks;

namespace Clockwork.Runtime.Threading;

/// <summary>
/// <para>
/// Static shims for the <see cref="Thread"/> surface. The rewriter redirects the supported call sites
/// here: each <c>new Thread(...)</c> becomes <see cref="Create(ThreadStart)"/> and the instance members
/// (<c>Start</c>, <c>Join</c>) become static methods whose first parameter is the receiver, matching
/// Clockwork's <c>RedirectCall</c> convention; the static members (<c>Sleep</c>, <c>Yield</c>,
/// <c>SpinWait</c>) stay static.
/// </para>
/// <para>
/// A controlled thread is a real <see cref="Thread"/> object (so the logical identity surface -
/// <see cref="Thread.Name"/>, <see cref="Thread.ManagedThreadId"/>, <see cref="Thread.IsBackground"/> -
/// keeps working unchanged) whose delegate body is <em>never run on a physical OS thread</em>. Instead
/// <see cref="Create(ThreadStart)"/> records the start delegate against the thread object, and
/// <see cref="Start(Thread)"/> queues that body as a fresh controlled operation on the simulation
/// coordinator (exactly as <see cref="ControlledTask.Run(System.Action)"/> and
/// <see cref="ControlledTaskFactory"/> do), so it runs deterministically on the single logical thread.
/// <see cref="Join(Thread)"/> pumps the deterministic loop until that operation completes rather than
/// blocking a physical thread.
/// </para>
/// <para>
/// This is the cooperative analogue of Microsoft Coyote's controlled <c>Thread</c>
/// (<c>Microsoft.Coyote.Rewriting.Types.Threading.Thread</c>, MIT-licensed): Coyote runs each controlled
/// thread on a real OS thread gated by a physical baton, whereas Clockwork schedules the body as a
/// cooperative controlled operation. The observable single-logical-thread interleaving is the same; the
/// deviation is documented in <c>docs/compatibility.md</c>. The operating-system-specific surface
/// (priority, apartment state, abort/suspend/resume/interrupt) cannot be modelled faithfully and is
/// rejected precisely - see <see cref="ControlledThreadUnsupportedException"/>.
/// </para>
/// </summary>
public static class ControlledThread
{
    private const string StartApi = "System.Threading.Thread.Start";
    private const string JoinApi = "System.Threading.Thread.Join";
    private const string SleepApi = "System.Threading.Thread.Sleep";

    private sealed class InfiniteSleepException : Exception
    {
    }

    private sealed class Registration
    {
        public required Delegate Body { get; init; }

        public TaskCompletionSource Completion { get; } = new();

        public bool Started { get; set; }
    }

    // Weakly associates each controlled thread object with its recorded start delegate and completion, so
    // Start can schedule the body and Join can wait for it without ever touching a physical OS thread.
    private static readonly ConditionalWeakTable<Thread, Registration> Registry = new();

    /// <summary>Controlled <c>new Thread(ThreadStart)</c>.</summary>
    /// <param name="start">The delegate the thread runs.</param>
    /// <returns>A real thread object whose body is scheduled cooperatively when started under simulation.</returns>
    public static Thread Create(ThreadStart start)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread..ctor");
        ArgumentNullException.ThrowIfNull(start);
        var thread = new Thread(start);
        Registry.AddOrUpdate(thread, new Registration { Body = start });
        return thread;
    }

    /// <summary>Controlled <c>new Thread(ThreadStart, int)</c>.</summary>
    /// <param name="start">The delegate the thread runs.</param>
    /// <param name="maxStackSize">The requested maximum stack size for the identity thread object.</param>
    /// <returns>A real thread object whose body is scheduled cooperatively when started under simulation.</returns>
    public static Thread Create(ThreadStart start, int maxStackSize)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread..ctor");
        ArgumentNullException.ThrowIfNull(start);
        var thread = new Thread(start, maxStackSize);
        Registry.AddOrUpdate(thread, new Registration { Body = start });
        return thread;
    }

    /// <summary>Controlled <c>new Thread(ParameterizedThreadStart)</c>.</summary>
    /// <param name="start">The delegate the thread runs.</param>
    /// <returns>A real thread object whose body is scheduled cooperatively when started under simulation.</returns>
    public static Thread Create(ParameterizedThreadStart start)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread..ctor");
        ArgumentNullException.ThrowIfNull(start);
        var thread = new Thread(start);
        Registry.AddOrUpdate(thread, new Registration { Body = start });
        return thread;
    }

    /// <summary>Controlled <c>new Thread(ParameterizedThreadStart, int)</c>.</summary>
    /// <param name="start">The delegate the thread runs.</param>
    /// <param name="maxStackSize">The requested maximum stack size for the identity thread object.</param>
    /// <returns>A real thread object whose body is scheduled cooperatively when started under simulation.</returns>
    public static Thread Create(ParameterizedThreadStart start, int maxStackSize)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread..ctor");
        ArgumentNullException.ThrowIfNull(start);
        var thread = new Thread(start, maxStackSize);
        Registry.AddOrUpdate(thread, new Registration { Body = start });
        return thread;
    }

    /// <summary>Controlled <c>thread.Start()</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    public static void Start(Thread instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(StartApi);
        ArgumentNullException.ThrowIfNull(instance);
        StartControlled(instance, parameter: null, parameterSupplied: false);
    }

    /// <summary>Controlled <c>thread.Start(object)</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="parameter">The object passed to a <see cref="ParameterizedThreadStart"/> body.</param>
    public static void Start(Thread instance, object? parameter)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(StartApi);
        ArgumentNullException.ThrowIfNull(instance);
        StartControlled(instance, parameter, parameterSupplied: true);
    }

    /// <summary>Controlled <c>thread.Join()</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    public static void Join(Thread instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(JoinApi);
        ArgumentNullException.ThrowIfNull(instance);
        JoinControlled(instance);
    }

    /// <summary>Controlled <c>thread.Join(int)</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="millisecondsTimeout">The timeout (modelled as infinite inside a simulation; virtual-time timeouts are Phase 8).</param>
    /// <returns><see langword="true"/> once the thread has terminated.</returns>
    public static bool Join(Thread instance, int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(JoinApi);
        ArgumentNullException.ThrowIfNull(instance);
        ValidateTimeout(millisecondsTimeout, nameof(millisecondsTimeout));
        return JoinControlled(instance, millisecondsTimeout);
    }

    /// <summary>Controlled <c>thread.Join(TimeSpan)</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="timeout">The timeout (modelled as infinite inside a simulation; virtual-time timeouts are Phase 8).</param>
    /// <returns><see langword="true"/> once the thread has terminated.</returns>
    public static bool Join(Thread instance, TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(JoinApi);
        ArgumentNullException.ThrowIfNull(instance);
        int millisecondsTimeout = ValidateTimeout(timeout, nameof(timeout));
        return JoinControlled(instance, millisecondsTimeout);
    }

    /// <summary>Controlled <c>Thread.Sleep(int)</c>: cooperatively yields without blocking or using real time.</summary>
    /// <param name="millisecondsTimeout">The requested sleep duration (its length is a virtual-time concern owned by Phase 8).</param>
    public static void Sleep(int millisecondsTimeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SleepApi);
        ValidateTimeout(millisecondsTimeout, nameof(millisecondsTimeout));
        SleepControlled(millisecondsTimeout);
    }

    /// <summary>Controlled <c>Thread.Sleep(TimeSpan)</c>: cooperatively yields without blocking or using real time.</summary>
    /// <param name="timeout">The requested sleep duration (its length is a virtual-time concern owned by Phase 8).</param>
    public static void Sleep(TimeSpan timeout)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation(SleepApi);
        SleepControlled(ValidateTimeout(timeout, nameof(timeout)));
    }

    /// <summary>Controlled <c>Thread.SpinWait(int)</c>: a no-op cooperative hint inside a simulation.</summary>
    /// <param name="iterations">The requested spin iterations (ignored inside a simulation).</param>
    public static void SpinWait(int iterations)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.SpinWait");
    }

    /// <summary>Controlled <c>Thread.Yield()</c>.</summary>
    /// <returns>
    /// The cooperative loop result: the scheduler switches at explicit scheduling points, so a
    /// synchronous yield reports that no OS-level switch occurred.
    /// </returns>
    public static bool Yield()
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.Yield");
        return ControlledTaskRuntime.RunOne("System.Threading.Thread.Yield");
    }

    // ---- OS-specific surface that cannot be modelled faithfully: rejected precisely under simulation ----

    /// <summary>Rejected controlled <c>thread.Priority = value</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="priority">The requested priority.</param>
    public static void SetPriority(Thread instance, ThreadPriority priority)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.set_Priority");
        ArgumentNullException.ThrowIfNull(instance);
        throw Unsupported(
            "System.Threading.Thread.set_Priority",
            "OS thread priority has no meaning for a cooperatively-scheduled controlled operation, and " +
            "honouring it would imply a preemptive priority model the deterministic scheduler does not have.");
    }

    /// <summary>Rejected controlled <c>thread.Interrupt()</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    public static void Interrupt(Thread instance)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.Interrupt");
        ArgumentNullException.ThrowIfNull(instance);
        throw Unsupported(
            "System.Threading.Thread.Interrupt",
            "asynchronous interruption of a blocked thread cannot be modelled by the cooperative scheduler; " +
            "use cooperative cancellation instead.");
    }

    /// <summary>Rejected controlled <c>thread.SetApartmentState(ApartmentState)</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="state">The requested apartment state.</param>
    public static void SetApartmentState(Thread instance, ApartmentState state)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.SetApartmentState");
        ArgumentNullException.ThrowIfNull(instance);
        throw Unsupported(
            "System.Threading.Thread.SetApartmentState",
            "COM apartment state is an OS-thread concept with no analogue for a controlled operation.");
    }

    /// <summary>Rejected controlled <c>thread.TrySetApartmentState(ApartmentState)</c>.</summary>
    /// <param name="instance">The receiving thread.</param>
    /// <param name="state">The requested apartment state.</param>
    /// <returns>Never returns inside a simulation; throws instead.</returns>
    public static bool TrySetApartmentState(Thread instance, ApartmentState state)
    {
        SimulationRuntimeDispatch.RequireActiveSimulation("System.Threading.Thread.TrySetApartmentState");
        ArgumentNullException.ThrowIfNull(instance);
        throw Unsupported(
            "System.Threading.Thread.TrySetApartmentState",
            "COM apartment state is an OS-thread concept with no analogue for a controlled operation.");
    }

    private static void StartControlled(Thread instance, object? parameter, bool parameterSupplied)
    {
        if (!Registry.TryGetValue(instance, out Registration? registration))
        {
            throw Unsupported(
                StartApi,
                "the thread was not created through the controlled Thread surface, so its body is unknown " +
                "and cannot be scheduled on the simulation coordinator (starting the real OS thread would " +
                "escape the single logical thread).");
        }

        if (registration.Started)
        {
            throw new ThreadStateException("The controlled thread has already been started.");
        }

        if (parameterSupplied && registration.Body is not ParameterizedThreadStart)
        {
            throw new InvalidOperationException(
                "The thread was created with a ThreadStart delegate and cannot be started with a parameter.");
        }

        registration.Started = true;
        ControlledTaskRuntime.QueueWork(() => RunBody(registration, parameter), StartApi);
    }

    private static void RunBody(Registration registration, object? parameter)
    {
        try
        {
            switch (registration.Body)
            {
                case ThreadStart threadStart:
                    threadStart();
                    break;
                case ParameterizedThreadStart parameterized:
                    parameterized(parameter);
                    break;
            }

            registration.Completion.TrySetResult();
        }
        catch (InfiniteSleepException)
        {
            // The strand remains represented by the never-ready wait registered by Sleep.
        }
        catch (Exception exception)
        {
            // A real thread's unhandled exception terminates the process. Inside a simulation the fault is
            // recorded as the thread's completion so Join observes termination deterministically instead of
            // tearing down the host; the exception-hardening slice may escalate this to the drive loop.
            registration.Completion.TrySetException(exception);
        }
    }

    private static void JoinControlled(Thread instance)
    {
        if (!Registry.TryGetValue(instance, out Registration? registration))
        {
            throw Unsupported(
                JoinApi,
                "the thread was not created through the controlled Thread surface, so its completion is not " +
                "tracked by the simulation coordinator.");
        }

        if (!registration.Started)
        {
            throw new ThreadStateException("Thread has not been started.");
        }

        // Pumps the deterministic loop until the thread's body completes. If the thread was never started
        // (or its body can never complete) this surfaces as the standard controlled deadlock diagnostic
        // rather than a real-time hang.
        ControlledTaskRuntime.DrainUntilCompleted(registration.Completion.Task, JoinApi);
    }

    private static bool JoinControlled(Thread instance, int millisecondsTimeout)
    {
        Registration registration = GetStartedRegistration(instance);
        if (millisecondsTimeout == 0)
        {
            return registration.Completion.Task.IsCompleted;
        }

        if (millisecondsTimeout == Timeout.Infinite)
        {
            ControlledTaskRuntime.DrainUntilCompleted(registration.Completion.Task, JoinApi);
            return true;
        }

        IControlledTimeout timeout = ControlledTaskRuntime.RegisterTimeout(
            TimeSpan.FromMilliseconds(millisecondsTimeout),
            onElapsed: null,
            JoinApi);
        try
        {
            ControlledTaskRuntime.DrainUntil(
                () => registration.Completion.Task.IsCompleted || timeout.IsElapsed,
                JoinApi);
            return registration.Completion.Task.IsCompleted;
        }
        finally
        {
            timeout.Cancel();
        }
    }

    private static Registration GetStartedRegistration(Thread instance)
    {
        if (!Registry.TryGetValue(instance, out Registration? registration))
        {
            throw Unsupported(
                JoinApi,
                "the thread was not created through the controlled Thread surface, so its completion is not " +
                "tracked by the simulation coordinator.");
        }

        if (!registration.Started)
        {
            throw new ThreadStateException("Thread has not been started.");
        }

        return registration;
    }

    private static void SleepControlled(int millisecondsTimeout)
    {
        if (millisecondsTimeout == 0)
        {
            ControlledTaskRuntime.RunOne(SleepApi);
            return;
        }

        if (millisecondsTimeout == Timeout.Infinite)
        {
            ControlledTaskRuntime.ParkIndefinitely(SleepApi);
            throw new InfiniteSleepException();
        }

        IControlledTimeout timeout = ControlledTaskRuntime.RegisterTimeout(
            TimeSpan.FromMilliseconds(millisecondsTimeout),
            onElapsed: null,
            SleepApi);
        try
        {
            ControlledTaskRuntime.DrainUntil(() => timeout.IsElapsed, SleepApi);
        }
        finally
        {
            timeout.Cancel();
        }
    }

    private static int ValidateTimeout(TimeSpan timeout, string paramName)
    {
        long millisecondsTimeout = (long)timeout.TotalMilliseconds;
        if (millisecondsTimeout < Timeout.Infinite || millisecondsTimeout > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }

        return (int)millisecondsTimeout;
    }

    private static void ValidateTimeout(int millisecondsTimeout, string paramName)
    {
        if (millisecondsTimeout < Timeout.Infinite)
        {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }

    private static ControlledThreadUnsupportedException Unsupported(string apiName, string reason) =>
        new(apiName, reason);
}
