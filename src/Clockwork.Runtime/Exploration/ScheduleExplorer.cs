using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Runtime.Exploration;

/// <summary>Exploration stop reason.</summary>
public enum ExplorationTerminationReason
{
    /// <summary>Every configured iteration completed.</summary>
    IterationLimit,

    /// <summary>The first non-success outcome was found.</summary>
    FirstFailure,

    /// <summary>The configured failure count was reached.</summary>
    FailureLimit,

    /// <summary>The wall-clock safety bound was reached between deterministic iterations.</summary>
    TimeLimit,

    /// <summary>Exploration cancellation was requested.</summary>
    Canceled,
}

/// <summary>Configures bounded serial schedule exploration.</summary>
public sealed record ScheduleExplorationConfiguration
{
    /// <summary>Gets the stable model/application root seed held constant across all iterations.</summary>
    public required int RootSeed { get; init; }

    /// <summary>Gets the first explicit schedule seed. Later seeds are deterministic increments.</summary>
    public required int FirstScheduleSeed { get; init; }

    /// <summary>Gets the maximum number of iterations.</summary>
    public int MaxIterations { get; init; } = 100;

    /// <summary>Gets the maximum controlled steps per iteration.</summary>
    public int MaxStepsPerIteration { get; init; } = 1_000_000;

    /// <summary>Gets whether exploration stops at the first non-success outcome.</summary>
    public bool StopOnFirstFailure { get; init; } = true;

    /// <summary>Gets the maximum number of non-success outcomes to collect.</summary>
    public int MaxFailures { get; init; } = int.MaxValue;

    /// <summary>
    /// Gets an optional wall-clock safety bound. It is checked only between iterations, so an
    /// iteration and its artifact are never truncated by this bound.
    /// </summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>
    /// Gets requested exploration parallelism. Only serial execution is supported because scheduler
    /// and instrumentation isolation across concurrent iterations is not assumed.
    /// </summary>
    public int Parallelism { get; init; } = 1;

    /// <summary>Gets the instrumentation closure identity, when applicable.</summary>
    public ReplayInstrumentationIdentity? Instrumentation { get; init; }
}

/// <summary>One deterministic exploration iteration.</summary>
public sealed record ScheduleExplorationIteration
{
    /// <summary>Gets the zero-based iteration index.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the stable iteration identity.</summary>
    public required string IterationId { get; init; }

    /// <summary>Gets the schedule seed varied for this iteration.</summary>
    public required int ScheduleSeed { get; init; }

    /// <summary>Gets the execution result and replay artifact.</summary>
    public required ReplayExecutionResult Execution { get; init; }

    /// <summary>Gets whether this iteration produced a non-success outcome.</summary>
    public bool IsFailure => Execution.Artifact.Outcome.Kind != ReplayTerminationKind.Completed;
}

/// <summary>Aggregate results from bounded schedule exploration.</summary>
public sealed record ScheduleExplorationResult
{
    /// <summary>Gets why exploration stopped.</summary>
    public required ExplorationTerminationReason TerminationReason { get; init; }

    /// <summary>Gets completed iterations in deterministic index order.</summary>
    public required IReadOnlyList<ScheduleExplorationIteration> Iterations { get; init; }

    /// <summary>Gets outcome counts in enum order.</summary>
    public required IReadOnlyDictionary<ReplayTerminationKind, int> OutcomeCounts { get; init; }

    /// <summary>
    /// Gets the smallest retained artifact for each stable failure category/identity. Size is compared
    /// by decision count, then race-point count, then artifact id.
    /// </summary>
    public required IReadOnlyDictionary<string, ReplayExecutionResult> RetainedFailures { get; init; }

    /// <summary>Gets the number of non-success outcomes observed.</summary>
    public int FailureCount => Iterations.Count(static iteration => iteration.IsFailure);
}

/// <summary>Runs bounded serial exploration while keeping model/application seeds fixed.</summary>
public static class ScheduleExplorer
{
    /// <summary>Explores schedule seeds serially.</summary>
    public static ScheduleExplorationResult Explore(
        ScheduleExplorationConfiguration configuration,
        Action<ControlledOperationScheduler> scenario) =>
        Explore(configuration, scenario, CancellationToken.None);

    /// <summary>Explores schedule seeds serially with explicit cancellation.</summary>
    public static ScheduleExplorationResult Explore(
        ScheduleExplorationConfiguration configuration,
        Action<ControlledOperationScheduler> scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(scenario);
        Validate(configuration);

        var stopwatch = Stopwatch.StartNew();
        var iterations = new List<ScheduleExplorationIteration>(configuration.MaxIterations);
        var counts = new SortedDictionary<ReplayTerminationKind, int>();
        var retained = new SortedDictionary<string, ReplayExecutionResult>(StringComparer.Ordinal);
        ExplorationTerminationReason terminationReason = ExplorationTerminationReason.IterationLimit;
        var failureCount = 0;

        for (var index = 0; index < configuration.MaxIterations; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                terminationReason = ExplorationTerminationReason.Canceled;
                break;
            }

            if (configuration.TimeLimit is { } timeLimit && stopwatch.Elapsed >= timeLimit)
            {
                terminationReason = ExplorationTerminationReason.TimeLimit;
                break;
            }

            int scheduleSeed = DeriveScheduleSeed(configuration.FirstScheduleSeed, index);
            ReplayExecutionResult execution = ReplayRunner.Record(
                new ReplayRunConfiguration
                {
                    RootSeed = configuration.RootSeed,
                    SchedulingPolicy = ReplaySchedulingPolicy.SeededRandom,
                    ScheduleSeed = scheduleSeed,
                    MaxSteps = configuration.MaxStepsPerIteration,
                    Instrumentation = configuration.Instrumentation,
                },
                scenario,
                cancellationToken);
            var iteration = new ScheduleExplorationIteration
            {
                Index = index,
                IterationId = CreateIterationId(configuration.RootSeed, scheduleSeed, index),
                ScheduleSeed = scheduleSeed,
                Execution = execution,
            };
            iterations.Add(iteration);
            counts[execution.Artifact.Outcome.Kind] =
                counts.GetValueOrDefault(execution.Artifact.Outcome.Kind) + 1;

            if (!iteration.IsFailure)
            {
                continue;
            }

            failureCount++;
            RetainSmallest(retained, execution);
            if (configuration.StopOnFirstFailure)
            {
                terminationReason = ExplorationTerminationReason.FirstFailure;
                break;
            }

            if (failureCount >= configuration.MaxFailures)
            {
                terminationReason = ExplorationTerminationReason.FailureLimit;
                break;
            }
        }

        return new ScheduleExplorationResult
        {
            TerminationReason = terminationReason,
            Iterations = iterations,
            OutcomeCounts = counts,
            RetainedFailures = retained,
        };
    }

    /// <summary>Derives the schedule seed for a zero-based iteration.</summary>
    public static int DeriveScheduleSeed(int firstScheduleSeed, int iteration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iteration);
        return unchecked(firstScheduleSeed + iteration);
    }

    /// <summary>Creates a stable iteration identity from root seed, schedule seed, and index.</summary>
    public static string CreateIterationId(int rootSeed, int scheduleSeed, int iteration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(iteration);
        string input = string.Create(
            CultureInfo.InvariantCulture,
            $"{rootSeed}:{scheduleSeed}:{iteration}");
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..12];
        return string.Create(CultureInfo.InvariantCulture, $"iteration-{iteration:D6}-{hash}");
    }

    private static void RetainSmallest(
        SortedDictionary<string, ReplayExecutionResult> retained,
        ReplayExecutionResult candidate)
    {
        ReplayOutcome outcome = candidate.Artifact.Outcome;
        string key = $"{outcome.Kind}:{outcome.FailureIdentity ?? "<none>"}";
        if (!retained.TryGetValue(key, out ReplayExecutionResult? current) ||
            Compare(candidate, current) < 0)
        {
            retained[key] = candidate;
        }
    }

    private static int Compare(ReplayExecutionResult left, ReplayExecutionResult right)
    {
        int byDecisions = left.Artifact.Decisions.Count.CompareTo(right.Artifact.Decisions.Count);
        if (byDecisions != 0)
        {
            return byDecisions;
        }

        int byRacePoints = left.Artifact.RaceSchedulingPoints.Count.CompareTo(
            right.Artifact.RaceSchedulingPoints.Count);
        return byRacePoints != 0
            ? byRacePoints
            : StringComparer.Ordinal.Compare(left.ArtifactId, right.ArtifactId);
    }

    private static void Validate(ScheduleExplorationConfiguration configuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaxIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaxStepsPerIteration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaxFailures);
        if (configuration.TimeLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuration),
                "TimeLimit must be positive when specified.");
        }

        if (configuration.Parallelism != 1)
        {
            throw new NotSupportedException(
                "Schedule exploration currently requires Parallelism=1 so iteration isolation and replay order remain reproducible.");
        }
    }
}
