using System.Globalization;
using System.Text;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Scheduling;

namespace Clockwork.Testing;

/// <summary>Environment variable names understood by replay-aware test fixtures.</summary>
public static class ReplayTestEnvironment
{
    /// <summary>Path to a complete artifact which should be replayed instead of recording a new run.</summary>
    public const string Artifact = "CLOCKWORK_REPLAY_ARTIFACT";

    /// <summary>Optional root seed override.</summary>
    public const string RootSeed = "CLOCKWORK_ROOT_SEED";

    /// <summary>Optional schedule seed override.</summary>
    public const string ScheduleSeed = "CLOCKWORK_SCHEDULE_SEED";

    /// <summary>Optional directory for failed-test artifacts.</summary>
    public const string ArtifactDirectory = "CLOCKWORK_ARTIFACT_DIRECTORY";
}

/// <summary>Configures a framework-neutral replay-aware test fixture.</summary>
public sealed record ReplayTestConfiguration
{
    /// <summary>Gets the stable test class/suite identity.</summary>
    public required string TestClassName { get; init; }

    /// <summary>Gets the stable test method/case identity.</summary>
    public required string TestMethodName { get; init; }

    /// <summary>Gets an explicit root seed, overriding test-identity and environment derivation.</summary>
    public int? RootSeed { get; init; }

    /// <summary>Gets an explicit schedule seed, overriding environment derivation.</summary>
    public int? ScheduleSeed { get; init; }

    /// <summary>Gets the artifact directory, overriding the environment and temporary default.</summary>
    public string? ArtifactDirectory { get; init; }

    /// <summary>Gets the controlled step bound.</summary>
    public int MaxSteps { get; init; } = 1_000_000;

    /// <summary>Gets whether successful recordings should also be written.</summary>
    public bool RecordSuccessfulRuns { get; init; }

    /// <summary>Gets the instrumentation compatibility identity, when applicable.</summary>
    public ReplayInstrumentationIdentity? Instrumentation { get; init; }
}

/// <summary>A file attachment produced by a replay-aware test fixture.</summary>
public sealed record ReplayTestAttachment(string Path, string Description);

/// <summary>The structured result and reproducibility data for one replay-aware test run.</summary>
public sealed record ReplayTestResult
{
    /// <summary>Gets the test root seed.</summary>
    public required int RootSeed { get; init; }

    /// <summary>Gets the schedule seed.</summary>
    public required int ScheduleSeed { get; init; }

    /// <summary>Gets the record/replay execution result.</summary>
    public required ReplayExecutionResult Execution { get; init; }

    /// <summary>Gets the written or replayed artifact path, when available.</summary>
    public string? ArtifactPath { get; init; }

    /// <summary>Gets artifact attachments for a test framework or CI adapter.</summary>
    public IReadOnlyList<ReplayTestAttachment> Attachments =>
        ArtifactPath is null
            ? []
            : [new ReplayTestAttachment(ArtifactPath, "Clockwork replay artifact")];

    /// <summary>Gets whether the scenario completed without a failure outcome.</summary>
    public bool IsSuccessful => Execution.Artifact.Outcome.Kind == ReplayTerminationKind.Completed;

    /// <summary>Formats a stable failure message containing seed and artifact attachment information.</summary>
    public string ToFailureMessage()
    {
        var builder = new StringBuilder();
        builder.Append("Clockwork test outcome: ").Append(Execution.Artifact.Outcome.Kind).Append('\n');
        builder.Append("Root seed: ").Append(RootSeed).Append('\n');
        builder.Append("Schedule seed: ").Append(ScheduleSeed).Append('\n');
        builder.Append("Failure identity: ")
            .Append(Execution.Artifact.Outcome.FailureIdentity ?? "none")
            .Append('\n');
        builder.Append("Replay artifact: ").Append(ArtifactPath ?? "not written");
        if (ArtifactPath is not null)
        {
            builder.Append('\n').Append("Environment replay: ")
                .Append(ReplayTestEnvironment.Artifact).Append('=').Append(ArtifactPath);
        }

        return builder.ToString();
    }

    /// <summary>Throws a framework-neutral test failure exception when the scenario did not complete.</summary>
    public void ThrowIfFailed()
    {
        if (!IsSuccessful)
        {
            throw new ReplayTestFailureException(this);
        }
    }

    /// <summary>Formats explicit CLI arguments for this artifact and scenario harness.</summary>
    public string GetReplayCommand(string assemblyPath, string scenarioType)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        ArgumentException.ThrowIfNullOrEmpty(scenarioType);
        if (ArtifactPath is null)
        {
            throw new InvalidOperationException("No replay artifact path is available.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"clockwork replay \"{ArtifactPath}\" --assembly \"{assemblyPath}\" --scenario-type \"{scenarioType}\"");
    }
}

/// <summary>Exception thrown by <see cref="ReplayTestResult.ThrowIfFailed"/>.</summary>
public sealed class ReplayTestFailureException : Exception
{
    /// <summary>Initializes a replay-aware test failure.</summary>
    public ReplayTestFailureException(ReplayTestResult result)
        : base(result?.ToFailureMessage())
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>Gets the structured test result.</summary>
    public ReplayTestResult Result { get; }
}

/// <summary>
/// Records failed controlled scenarios and replays an artifact selected through
/// <see cref="ReplayTestEnvironment.Artifact"/>.
/// </summary>
public sealed class ReplayTestFixture
{
    private readonly ReplayTestConfiguration _configuration;

    /// <summary>Initializes a replay-aware fixture.</summary>
    public ReplayTestFixture(ReplayTestConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrEmpty(configuration.TestClassName);
        ArgumentException.ThrowIfNullOrEmpty(configuration.TestMethodName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuration.MaxSteps);
        _configuration = configuration;
    }

    /// <summary>Gets the stable seed derived by the existing simulation test-identity helper.</summary>
    public int TestIdentitySeed => SimulationSeed.FromStrings(
        _configuration.TestClassName,
        _configuration.TestMethodName);

    /// <summary>Records or environment-replays a fresh controlled scenario.</summary>
    public ReplayTestResult Run(Action<ControlledOperationScheduler> scenario) =>
        Run(scenario, CancellationToken.None);

    /// <summary>Records or environment-replays a fresh controlled scenario with explicit cancellation.</summary>
    public ReplayTestResult Run(
        Action<ControlledOperationScheduler> scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        string? replayPath = Environment.GetEnvironmentVariable(ReplayTestEnvironment.Artifact);
        if (!string.IsNullOrWhiteSpace(replayPath))
        {
            ReplayArtifact artifact = ReplayArtifactSerializer.Read(replayPath);
            ReplayExecutionResult replay = ReplayRunner.Replay(
                artifact,
                ReplayCompatibilityRequirements.Current() with
                {
                    Instrumentation = _configuration.Instrumentation,
                },
                scenario,
                _configuration.MaxSteps,
                cancellationToken);
            return new ReplayTestResult
            {
                RootSeed = artifact.RootSeed,
                ScheduleSeed = artifact.Scheduler.ScheduleSeed ?? 0,
                Execution = replay,
                ArtifactPath = Path.GetFullPath(replayPath),
            };
        }

        int rootSeed = _configuration.RootSeed ??
            ParseEnvironmentSeed(ReplayTestEnvironment.RootSeed) ??
            TestIdentitySeed;
        int scheduleSeed = _configuration.ScheduleSeed ??
            ParseEnvironmentSeed(ReplayTestEnvironment.ScheduleSeed) ??
            new SimulationSeedAuthority(rootSeed).GetDomainSeed(SimulationSeedDomain.Scheduler);
        ReplayExecutionResult execution = ReplayRunner.Record(
            new ReplayRecordingOptions
            {
                RootSeed = rootSeed,
                SchedulingPolicy = ReplaySchedulingPolicy.SeededRandom,
                ScheduleSeed = scheduleSeed,
                MaxSteps = _configuration.MaxSteps,
                Instrumentation = _configuration.Instrumentation,
            },
            scenario,
            cancellationToken);

        string? artifactPath = null;
        if (_configuration.RecordSuccessfulRuns ||
            execution.Artifact.Outcome.Kind != ReplayTerminationKind.Completed)
        {
            string directory = _configuration.ArtifactDirectory ??
                Environment.GetEnvironmentVariable(ReplayTestEnvironment.ArtifactDirectory) ??
                Path.Combine(Path.GetTempPath(), "clockwork-test-artifacts");
            string safeIdentity = SanitizeFileName(
                _configuration.TestClassName + "." + _configuration.TestMethodName);
            string artifactId = execution.ArtifactId[..12];
            artifactPath = Path.GetFullPath(Path.Combine(directory, $"{safeIdentity}-{artifactId}.cwr.json"));
            ReplayArtifactSerializer.Write(artifactPath, execution.Artifact);
        }

        return new ReplayTestResult
        {
            RootSeed = rootSeed,
            ScheduleSeed = scheduleSeed,
            Execution = execution,
            ArtifactPath = artifactPath,
        };
    }

    private static int? ParseEnvironmentSeed(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
        {
            throw new InvalidOperationException(
                $"Environment variable {name} must be a 32-bit integer, not '{value}'.");
        }

        return seed;
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }
}
