using System.Text;
using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Random;
using Clockwork.Runtime.Replay;
using Clockwork.Runtime.Racing;

namespace Clockwork.Runtime.Tests.Replay;

public sealed class ReplayArtifactTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clockwork-replay", Guid.NewGuid().ToString("n"));

    [Fact]
    public void CanonicalJsonIsStableAndRoundTrips()
    {
        ReplayArtifact artifact = CreateArtifact();

        byte[] first = ReplayArtifactSerializer.Serialize(artifact);
        ReplayArtifact roundTripped = ReplayArtifactSerializer.Deserialize(first);
        byte[] second = ReplayArtifactSerializer.Serialize(roundTripped);

        Assert.Equal(first, second);
        Assert.Equal(ReplayArtifactSerializer.ComputeId(artifact), ReplayArtifactSerializer.ComputeId(roundTripped));
        Assert.DoesNotContain(Environment.CommandLine, Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalJsonSortsSchedulerOptions()
    {
        ReplayArtifact artifact = CreateArtifact() with
        {
            Scheduler = new ReplaySchedulerConfiguration
            {
                Strategy = "seeded-random",
                ScheduleSeed = 17,
                Options = new Dictionary<string, string>
                {
                    ["zeta"] = "last",
                    ["alpha"] = "first",
                },
            },
        };

        string json = ReplayArtifactSerializer.ToJson(artifact);

        Assert.True(
            json.IndexOf("\"alpha\"", StringComparison.Ordinal) <
            json.IndexOf("\"zeta\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FileWriteIsCanonicalAndReplaceable()
    {
        string path = Path.Combine(_root, "run.cwr.json");
        ReplayArtifact artifact = CreateArtifact();

        ReplayArtifactSerializer.Write(path, artifact);
        ReplayArtifactSerializer.Write(path, artifact);

        Assert.Equal(ReplayArtifactSerializer.Serialize(artifact), File.ReadAllBytes(path));
        Assert.Equal(
            ReplayArtifactSerializer.Serialize(artifact),
            ReplayArtifactSerializer.Serialize(ReplayArtifactSerializer.Read(path)));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"other\",\"schemaVersion\":1}")]
    public void CorruptOrIncompleteJsonIsRejected(string json)
    {
        Assert.Throws<ReplayArtifactFormatException>(
            () => ReplayArtifactSerializer.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void OversizedDocumentIsRejectedBeforeParsing()
    {
        byte[] content = new byte[ReplayArtifactLimits.MaxDocumentBytes + 1];

        ReplayArtifactFormatException exception = Assert.Throws<ReplayArtifactFormatException>(
            () => ReplayArtifactSerializer.Deserialize(content));

        Assert.Contains("limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonContiguousDecisionStreamIsRejected()
    {
        ReplayArtifact artifact = CreateArtifact() with
        {
            Decisions =
            [
                CreateDecision(0),
                CreateDecision(2),
            ],
        };

        ReplayArtifactFormatException exception = Assert.Throws<ReplayArtifactFormatException>(
            () => ReplayArtifactSerializer.Serialize(artifact));

        Assert.Contains("contiguous", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AbortedRecordingCannotClaimTerminalCompletion()
    {
        ReplayArtifact artifact = CreateArtifact() with { RecordingState = ReplayRecordingState.Aborted };

        ReplayArtifactFormatException exception = Assert.Throws<ReplayArtifactFormatException>(
            () => ReplayArtifactSerializer.Serialize(artifact));

        Assert.Contains("Aborted outcome", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityMismatchFailsBeforeReplay()
    {
        ReplayArtifact artifact = CreateArtifact();
        ReplayCompatibilityRequirements requirements = ReplayCompatibilityRequirements.Current() with
        {
            RuntimeCompatibility = ".NETCoreApp,Version=v99.0",
        };

        ReplayCompatibilityException exception = Assert.Throws<ReplayCompatibilityException>(
            () => ReplayCompatibility.Validate(artifact, requirements));

        Assert.Contains("runtime compatibility mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AbortedRecordingIsRejectedForReplay()
    {
        ReplayArtifact artifact = CreateArtifact() with
        {
            RecordingState = ReplayRecordingState.Aborted,
            Outcome = new ReplayOutcome { Kind = ReplayTerminationKind.Aborted },
        };

        Assert.Throws<ReplayCompatibilityException>(
            () => ReplayCompatibility.Validate(artifact, ReplayCompatibilityRequirements.Current()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ReplayArtifact CreateArtifact() => new()
    {
        RecordingState = ReplayRecordingState.Complete,
        RootSeed = 42,
        Scheduler = new ReplaySchedulerConfiguration
        {
            Strategy = "seeded-random",
            ScheduleSeed = 17,
        },
        Environment = ReplayCompatibility.CaptureEnvironment(),
        Decisions =
        [
            CreateDecision(0),
        ],
        RaceSchedulingPoints =
        [
            new ReplayRaceSchedulingPoint
            {
                Sequence = 1,
                OperationId = 2,
                Kind = RaceAccessKind.Write,
                Location = "field:Counter",
                Member = "Tests.Counter::Increment",
                ILOffset = 4,
                SourceFile = null,
                SourceLine = -1,
            },
        ],
        Outcome = new ReplayOutcome
        {
            Kind = ReplayTerminationKind.Faulted,
            FailureIdentity = "System.InvalidOperationException",
            Diagnostic = "known failure",
        },
    };

    private static ReplayDecision CreateDecision(long sequence) => new()
    {
        Sequence = sequence,
        Domain = SimulationSeedDomain.Scheduler,
        Kind = SimulationDecisionKind.SchedulingOrder,
        SourceId = "seeded-random",
        InputMetadata = "1,2",
        SelectedResult = "2",
        NodeId = null,
        LogicalExecutionId = 0,
    };
}
