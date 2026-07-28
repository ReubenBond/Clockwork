using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Execution;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Racing;
using Clockwork.Runtime.Scheduling;
using Clockwork.Runtime.Scheduling.Resources;
using Clockwork.Runtime.Scheduling.Strategies;

namespace Clockwork.Runtime.Replay;

/// <summary>Supported deterministic scheduling policies for recorded execution.</summary>
public enum ReplaySchedulingPolicy
{
    /// <summary>Select the earliest registered runnable operation and waiter.</summary>
    Fifo,

    /// <summary>Rotate fairly among runnable operations and use FIFO waiter order.</summary>
    RoundRobin,

    /// <summary>Prefer higher-priority operations with deterministic round-robin tie breaking.</summary>
    Priority,

    /// <summary>Choose runnable operations and resource waiters from one explicit seeded stream.</summary>
    SeededRandom,
}

/// <summary>Configures one recorded execution.</summary>
public sealed record ReplayRunConfiguration
{
    /// <summary>Gets the root model/application seed.</summary>
    public required int RootSeed { get; init; }

    /// <summary>Gets the scheduler policy.</summary>
    public ReplaySchedulingPolicy SchedulingPolicy { get; init; } = ReplaySchedulingPolicy.RoundRobin;

    /// <summary>
    /// Gets the schedule seed. When omitted for seeded-random scheduling, it is derived from
    /// <see cref="RootSeed"/> in the scheduler seed domain and recorded explicitly.
    /// </summary>
    public int? ScheduleSeed { get; init; }

    /// <summary>Gets the maximum number of controlled operation steps.</summary>
    public int MaxSteps { get; init; } = 1_000_000;

    /// <summary>Gets the instrumentation closure identity, when applicable.</summary>
    public ReplayInstrumentationIdentity? Instrumentation { get; init; }

    /// <summary>Gets whether bounded exception messages may be included in artifact diagnostics.</summary>
    public bool IncludeDiagnosticMessages { get; init; }

    /// <summary>Gets whether source document paths may be included in race scheduling points.</summary>
    public bool IncludeSourcePaths { get; init; }
}

/// <summary>Results from one record or replay execution.</summary>
public sealed record ReplayExecutionResult
{
    /// <summary>Gets the deterministic replay artifact.</summary>
    public required ReplayArtifact Artifact { get; init; }

    /// <summary>Gets the SHA-256 artifact identity.</summary>
    public required string ArtifactId { get; init; }

    /// <summary>Gets the number of controlled operation steps executed.</summary>
    public required int Steps { get; init; }

    /// <summary>Gets the original exception which aborted orchestration, when present.</summary>
    public Exception? ExecutionException { get; init; }

    /// <summary>Gets whether a replay reproduced the recorded terminal outcome.</summary>
    public bool Reproduced { get; init; }

    /// <summary>Gets a persisted artifact path, when the caller wrote it.</summary>
    public string? ArtifactPath { get; init; }

    /// <summary>Gets a persisted minimized artifact path, when available.</summary>
    public string? MinimizedArtifactPath { get; init; }
}

/// <summary>Thrown when replay reaches a different terminal outcome than the artifact.</summary>
public sealed class ReplayOutcomeMismatchException : InvalidOperationException
{
    /// <summary>Initializes a terminal outcome mismatch.</summary>
    public ReplayOutcomeMismatchException(ReplayOutcome expected, ReplayOutcome actual)
        : base(
            $"Replay terminal outcome diverged: expected {Describe(expected)}, observed {Describe(actual)}.")
    {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>Gets the recorded outcome.</summary>
    public ReplayOutcome Expected { get; }

    /// <summary>Gets the replay outcome.</summary>
    public ReplayOutcome Actual { get; }

    private static string Describe(ReplayOutcome outcome) =>
        $"{outcome.Kind} identity='{outcome.FailureIdentity ?? "<none>"}'";
}

/// <summary>
/// Records and replays controlled scheduler scenarios. The callback only registers controlled work;
/// this runner owns scheduler creation, driving, terminal classification, and replay validation.
/// </summary>
public static class ReplayRunner
{
    /// <summary>Records one controlled scenario into a complete or explicitly aborted artifact.</summary>
    public static ReplayExecutionResult Record(
        ReplayRunConfiguration configuration,
        Action<ControlledOperationScheduler> scenario) =>
        Record(configuration, scenario, CancellationToken.None);

    /// <summary>Records one controlled scenario with explicit orchestration cancellation.</summary>
    public static ReplayExecutionResult Record(
        ReplayRunConfiguration configuration,
        Action<ControlledOperationScheduler> scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(scenario);
        ValidateConfiguration(configuration);

        int scheduleSeed = GetScheduleSeed(configuration);
        var decisionLog = new SimulationDecisionLog();
        var listener = new ReplayOperationListener(configuration.IncludeDiagnosticMessages);
        using ControlledOperationScheduler scheduler = CreateScheduler(configuration.RootSeed, listener);
        scheduler.SchedulingStrategy = CreateStrategy(configuration.SchedulingPolicy, scheduleSeed);
        scheduler.DecisionLog = decisionLog;

        DriveResult drive = Drive(scheduler, scenario, configuration.MaxSteps, cancellationToken);
        ReplayArtifact artifact = CreateArtifact(
            configuration,
            scheduleSeed,
            decisionLog.Records,
            scheduler,
            listener,
            drive);
        return new ReplayExecutionResult
        {
            Artifact = artifact,
            ArtifactId = ReplayArtifactSerializer.ComputeId(artifact),
            Steps = drive.Steps,
            ExecutionException = drive.Exception,
            Reproduced = false,
        };
    }

    /// <summary>Replays a complete artifact exactly after validating compatibility.</summary>
    public static ReplayExecutionResult Replay(
        ReplayArtifact artifact,
        ReplayCompatibilityRequirements requirements,
        Action<ControlledOperationScheduler> scenario,
        int maxSteps = 1_000_000) =>
        Replay(artifact, requirements, scenario, maxSteps, CancellationToken.None);

    /// <summary>Replays a complete artifact exactly with explicit orchestration cancellation.</summary>
    public static ReplayExecutionResult Replay(
        ReplayArtifact artifact,
        ReplayCompatibilityRequirements requirements,
        Action<ControlledOperationScheduler> scenario,
        int maxSteps,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSteps);
        ReplayCompatibility.Validate(artifact, requirements);

        var records = artifact.Decisions.Select(static decision => decision.ToRecord()).ToArray();
        var listener = new ReplayOperationListener(includeDiagnosticMessages: false);
        using ControlledOperationScheduler scheduler = CreateScheduler(artifact.RootSeed, listener);
        scheduler.SchedulingStrategy = new ReplaySchedulingStrategy(records);
        scheduler.ReplayValidator = new SimulationDecisionReplayValidator(
            new SimulationInMemoryDecisionReplayReader(records));

        DriveResult drive = Drive(scheduler, scenario, maxSteps, cancellationToken);
        if (!drive.IsAborted)
        {
            scheduler.ValidateReplayDecisionStreamsComplete();
        }

        ReplayRunConfiguration replayConfiguration = new()
        {
            RootSeed = artifact.RootSeed,
            SchedulingPolicy = ParsePolicy(artifact.Scheduler.Strategy),
            ScheduleSeed = artifact.Scheduler.ScheduleSeed,
            MaxSteps = maxSteps,
            Instrumentation = artifact.Instrumentation,
        };
        ReplayArtifact replayArtifact = CreateArtifact(
            replayConfiguration,
            artifact.Scheduler.ScheduleSeed ?? 0,
            records,
            scheduler,
            listener,
            drive);
        if (!OutcomesMatch(artifact.Outcome, replayArtifact.Outcome))
        {
            throw new ReplayOutcomeMismatchException(artifact.Outcome, replayArtifact.Outcome);
        }

        return new ReplayExecutionResult
        {
            Artifact = artifact,
            ArtifactId = ReplayArtifactSerializer.ComputeId(artifact),
            Steps = drive.Steps,
            ExecutionException = drive.Exception,
            Reproduced = true,
        };
    }

    private static ControlledOperationScheduler CreateScheduler(int seed, IControlledOperationListener listener)
    {
        SimulationActivationToken token = SimulationRuntimeActivation.CreateToken();
        var runtime = new SimulationRuntimeIdentity(Guid.NewGuid(), seed, "replay-run");
        return new ControlledOperationScheduler(token, runtime, listener);
    }

    private static IControlledSchedulingStrategy CreateStrategy(ReplaySchedulingPolicy policy, int scheduleSeed) =>
        policy switch
        {
            ReplaySchedulingPolicy.Fifo => new FifoSchedulingStrategy(),
            ReplaySchedulingPolicy.RoundRobin => new RoundRobinSchedulingStrategy(),
            ReplaySchedulingPolicy.Priority => new PrioritySchedulingStrategy(),
            ReplaySchedulingPolicy.SeededRandom => new SeededRandomSchedulingStrategy(scheduleSeed),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    private static DriveResult Drive(
        ControlledOperationScheduler scheduler,
        Action<ControlledOperationScheduler> scenario,
        int maxSteps,
        CancellationToken cancellationToken)
    {
        var steps = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            scenario(scheduler);
            while (steps < maxSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scheduler.RunStep())
                {
                    steps++;
                    if (scheduler.FirstRace is not null ||
                        scheduler.CaptureStatus().Any(static status =>
                            status.State == ControlledOperationState.Faulted))
                    {
                        break;
                    }

                    continue;
                }

                if (!scheduler.TryAdvanceVirtualTime())
                {
                    break;
                }
            }

            return new DriveResult(
                steps,
                BoundExceeded: steps >= maxSteps && scheduler.PendingOperationCount > 0,
                Exception: null,
                IsAborted: false);
        }
        catch (Exception exception) when (
            exception is not SimulationDecisionReplayMismatchException &&
            exception is not ReplayOutcomeMismatchException)
        {
            return new DriveResult(steps, BoundExceeded: false, exception, IsAborted: true);
        }
    }

    private static ReplayArtifact CreateArtifact(
        ReplayRunConfiguration configuration,
        int scheduleSeed,
        IReadOnlyList<SimulationDecisionRecord> decisions,
        ControlledOperationScheduler scheduler,
        ReplayOperationListener listener,
        DriveResult drive)
    {
        ReplayOutcome outcome = ClassifyOutcome(configuration, scheduler, listener, drive);
        ReplayRecordingState state = drive.IsAborted
            ? ReplayRecordingState.Aborted
            : ReplayRecordingState.Complete;
        IReadOnlyList<ReplayRaceSchedulingPoint> points = scheduler.CaptureRaceSchedulingPoints()
            .Select(point =>
            {
                ReplayRaceSchedulingPoint replayPoint = ReplayRaceSchedulingPoint.FromPoint(point);
                return configuration.IncludeSourcePaths
                    ? replayPoint
                    : replayPoint with { SourceFile = null };
            })
            .ToArray();

        var schedulerOptions = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["maxSteps"] = configuration.MaxSteps.ToString(CultureInfo.InvariantCulture),
            ["resourceWaiters"] = configuration.SchedulingPolicy == ReplaySchedulingPolicy.SeededRandom
                ? "seeded-random"
                : "fifo",
        };

        return new ReplayArtifact
        {
            RecordingState = state,
            RootSeed = configuration.RootSeed,
            Scheduler = new ReplaySchedulerConfiguration
            {
                Strategy = GetStrategyName(configuration.SchedulingPolicy),
                ScheduleSeed = configuration.SchedulingPolicy == ReplaySchedulingPolicy.SeededRandom
                    ? scheduleSeed
                    : null,
                Options = schedulerOptions,
            },
            Instrumentation = configuration.Instrumentation,
            Environment = ReplayCompatibility.CaptureEnvironment(),
            Decisions = decisions.Select(static decision => ReplayDecision.FromRecord(decision)).ToArray(),
            RaceSchedulingPoints = points,
            Outcome = outcome,
        };
    }

    private static ReplayOutcome ClassifyOutcome(
        ReplayRunConfiguration configuration,
        ControlledOperationScheduler scheduler,
        ReplayOperationListener listener,
        DriveResult drive)
    {
        if (drive.IsAborted)
        {
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.Aborted,
                FailureIdentity = drive.Exception?.GetType().FullName,
                Diagnostic = configuration.IncludeDiagnosticMessages
                    ? Bound(drive.Exception?.Message)
                    : null,
            };
        }

        if (scheduler.FirstRace is { } race)
        {
            string identity = ComputeRaceIdentity(race);
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.RaceDetected,
                FailureIdentity = identity,
                Diagnostic = configuration.IncludeDiagnosticMessages ? Bound(race.ToString()) : null,
            };
        }

        if (listener.FirstFailure is { } failure)
        {
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.Faulted,
                FailureIdentity = failure.ExceptionType,
                Diagnostic = configuration.IncludeDiagnosticMessages ? Bound(failure.Message) : null,
            };
        }

        ControlledDeadlockReport deadlock = scheduler.DetectDeadlock();
        if (deadlock.IsDeadlocked)
        {
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.Deadlocked,
                FailureIdentity = ComputeDeadlockIdentity(deadlock),
                Diagnostic = configuration.IncludeDiagnosticMessages ? Bound(scheduler.DescribeLiveness()) : null,
            };
        }

        if (drive.BoundExceeded)
        {
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.BoundExceeded,
                FailureIdentity = "step-bound",
                Diagnostic = configuration.IncludeDiagnosticMessages
                    ? $"Controlled execution reached the {configuration.MaxSteps.ToString(CultureInfo.InvariantCulture)} step bound."
                    : null,
            };
        }

        if (scheduler.CaptureStatus().Any(static status => status.State == ControlledOperationState.Canceled))
        {
            return new ReplayOutcome
            {
                Kind = ReplayTerminationKind.Canceled,
                FailureIdentity = "controlled-operation-canceled",
            };
        }

        return new ReplayOutcome { Kind = ReplayTerminationKind.Completed };
    }

    private static bool OutcomesMatch(ReplayOutcome expected, ReplayOutcome actual) =>
        expected.Kind == actual.Kind &&
        string.Equals(expected.FailureIdentity, actual.FailureIdentity, StringComparison.Ordinal);

    private static string ComputeRaceIdentity(RaceReport race)
    {
        string first = string.Create(
            CultureInfo.InvariantCulture,
            $"{race.FirstAccess.Kind}|{race.FirstAccess.Source.Method}");
        string second = string.Create(
            CultureInfo.InvariantCulture,
            $"{race.SecondAccess.Kind}|{race.SecondAccess.Source.Method}");
        if (StringComparer.Ordinal.Compare(first, second) > 0)
        {
            (first, second) = (second, first);
        }

        string input = string.Create(
            CultureInfo.InvariantCulture,
            $"{race.FirstAccess.Location}|{first}|{second}");
        return "race:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];
    }

    private static string ComputeDeadlockIdentity(ControlledDeadlockReport report)
    {
        string input = string.Join(
            "|",
            report.Cycles.SelectMany(static cycle => cycle.Entries)
                .Select(static entry => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.OperationId.Value}>{entry.ResourceId.Value}>{entry.OwnerId.Value}")));
        return "deadlock:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..16];
    }

    private static string? Bound(string? value) =>
        value is null || value.Length <= ReplayArtifactLimits.MaxStringLength
            ? value
            : value[..ReplayArtifactLimits.MaxStringLength];

    private static int GetScheduleSeed(ReplayRunConfiguration configuration) =>
        configuration.SchedulingPolicy == ReplaySchedulingPolicy.SeededRandom
            ? configuration.ScheduleSeed ??
              new SimulationSeedAuthority(configuration.RootSeed).GetDomainSeed(SimulationSeedDomain.Scheduler)
            : 0;

    private static string GetStrategyName(ReplaySchedulingPolicy policy) =>
        policy switch
        {
            ReplaySchedulingPolicy.Fifo => "fifo",
            ReplaySchedulingPolicy.RoundRobin => "round-robin",
            ReplaySchedulingPolicy.Priority => "priority",
            ReplaySchedulingPolicy.SeededRandom => "seeded-random",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    private static ReplaySchedulingPolicy ParsePolicy(string strategy) =>
        strategy switch
        {
            "fifo" => ReplaySchedulingPolicy.Fifo,
            "round-robin" => ReplaySchedulingPolicy.RoundRobin,
            "priority" => ReplaySchedulingPolicy.Priority,
            "seeded-random" => ReplaySchedulingPolicy.SeededRandom,
            _ => throw new ReplayCompatibilityException($"Unsupported recorded scheduler strategy '{strategy}'."),
        };

    private static void ValidateConfiguration(ReplayRunConfiguration configuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaxSteps);
        if (configuration.SchedulingPolicy != ReplaySchedulingPolicy.SeededRandom &&
            configuration.ScheduleSeed is not null)
        {
            throw new ArgumentException(
                "ScheduleSeed is only valid with SeededRandom scheduling.",
                nameof(configuration));
        }
    }

    private sealed record DriveResult(int Steps, bool BoundExceeded, Exception? Exception, bool IsAborted);

    private sealed class ReplayOperationListener(bool includeDiagnosticMessages) : IControlledOperationListener
    {
        private readonly bool _includeDiagnosticMessages = includeDiagnosticMessages;

        public OperationFailure? FirstFailure { get; private set; }

        public void OnStateChanged(ControlledOperation operation, ControlledOperationState newState)
        {
            if (newState != ControlledOperationState.Faulted ||
                FirstFailure is not null ||
                operation.TerminalException is not { } exception)
            {
                return;
            }

            FirstFailure = new OperationFailure(
                exception.GetType().FullName ?? exception.GetType().Name,
                _includeDiagnosticMessages ? exception.Message : null);
        }
    }

    private sealed record OperationFailure(string ExceptionType, string? Message);
}
