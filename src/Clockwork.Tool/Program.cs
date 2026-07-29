using Clockwork.Instrumentation.Closure;
using Clockwork.Instrumentation.Configuration;
using Clockwork.Runtime.Decisions;
using Clockwork.Runtime.Exploration;
using Clockwork.Runtime.Replay;

namespace Clockwork.Tool;

/// <summary>
/// Entry point for the <c>clockwork</c> CLI. It dispatches to a command, maps every failure class to a
/// distinct <see cref="ExitCode"/>, and keeps output deterministic (no timestamps, stable ordering) so
/// it is scriptable and testable.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => (int)Run(args, Console.Out, Console.Error);

    /// <summary>Runs the CLI with explicit streams; used directly by tests.</summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="output">The standard output writer.</param>
    /// <param name="error">The standard error writer.</param>
    /// <returns>The process exit code.</returns>
    public static ExitCode Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage(output);
            return args.Length == 0 ? ExitCode.UsageError : ExitCode.Success;
        }

        if (args[0] is "--version")
        {
            output.WriteLine(Instrumentation.Rewriting.RewriteEngine.EngineVersion);
            return ExitCode.Success;
        }

        string command = args[0];
        string[] rest = args[1..];
        try
        {
            return command switch
            {
                "instrument" => InstrumentCommand.Run(rest, output, error),
                "inspect" => InspectCommand.Run(rest, output, error),
                "record" => ReplayCommands.RunRecord(rest, output),
                "replay" => ReplayCommands.RunReplay(rest, output),
                "explore" => ReplayCommands.RunExplore(rest, output),
                "minimize" => ReplayCommands.RunMinimize(rest, output),
                "trace" => ReplayCommands.RunTrace(rest, output),
                _ => Fail(error, $"Unknown command '{command}'.", ExitCode.UsageError),
            };
        }
        catch (UsageException ex)
        {
            return Fail(error, ex.Message, ExitCode.UsageError);
        }
        catch (ConfigurationException ex)
        {
            return Fail(error, ex.Message, ExitCode.ConfigurationError);
        }
        catch (RuleSetFormatException ex)
        {
            return Fail(error, ex.Message, ExitCode.ConfigurationError);
        }
        catch (ClosureException ex)
        {
            return Fail(error, ex.Message, ExitCode.ClosureError);
        }
        catch (ReplayMinimizationException ex)
        {
            return Fail(error, ex.Message, ExitCode.MinimizationError);
        }
        catch (Exception ex) when (
            ex is ReplayArtifactFormatException or
            ReplayCompatibilityException or
            SimulationDecisionReplayMismatchException or
            ReplayOutcomeMismatchException)
        {
            return Fail(error, ex.Message, ExitCode.ReplayError);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or BadImageFormatException)
        {
            return Fail(error, ex.Message, ExitCode.IoError);
        }
    }

    private static ExitCode Fail(TextWriter error, string message, ExitCode code)
    {
        error.WriteLine($"clockwork: {message}");
        return code;
    }

    private static bool IsHelp(string arg) => arg is "-h" or "--help" or "help";

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("clockwork - deterministic IL instrumentation tool");
        output.WriteLine();
        output.WriteLine("Usage:");
        output.WriteLine("  clockwork instrument --source <dir> --output <dir> [options]");
        output.WriteLine("  clockwork inspect <assembly|dir>... [options]");
        output.WriteLine("  clockwork record --assembly <path> --scenario-type <type> --artifact <path> --seed <int> [options]");
        output.WriteLine("  clockwork replay <artifact> --assembly <path> --scenario-type <type> [options]");
        output.WriteLine("  clockwork explore --assembly <path> --scenario-type <type> --output <dir> --seed <int> [options]");
        output.WriteLine("  clockwork minimize <artifact> --assembly <path> --scenario-type <type> [options]");
        output.WriteLine("  clockwork trace show <artifact> [--json]");
        output.WriteLine();
        output.WriteLine("instrument options:");
        output.WriteLine("  --source <dir>              application output/publish directory to read (required)");
        output.WriteLine("  --output <dir>             staging directory to write the instrumented closure (required unless --dry-run)");
        output.WriteLine("  --config <path>            JSON configuration file (source of policy settings)");
        output.WriteLine("  --rule-set <path>          rule-set JSON document (repeatable; appended to config)");
        output.WriteLine("  --include <glob>           include pattern (repeatable)");
        output.WriteLine("  --exclude <glob>           exclude pattern (repeatable)");
        output.WriteLine("  --entry <name>             entry assembly simple name (else auto-detected)");
        output.WriteLine("  --manifest <path>          manifest output path (else a sibling of --output)");
        output.WriteLine("  --mode <Controlled|RaceExploration> instrumentation granularity (default Controlled)");
        output.WriteLine("  --r2r <Reject|StripToIL>   ReadyToRun policy (default Reject)");
        output.WriteLine("  --strong-name <Fail|ReSign> legacy compatibility option (identities are stripped)");
        output.WriteLine("  --strong-name-key <path>   legacy compatibility option");
        output.WriteLine("  --exclude-framework <bool> exclude framework/reference assemblies (default true)");
        output.WriteLine("  --instrument-dependencies <bool> instrument managed dependencies (default true)");
        output.WriteLine("  --target-runtime <version> runtime version rules are evaluated against");
        output.WriteLine("  --builtin <id|all>         built-in rule set (repeatable)");
        output.WriteLine("  --builtin-include <family> include built-in family (repeatable)");
        output.WriteLine("  --builtin-exclude <family> exclude built-in family (repeatable)");
        output.WriteLine("  --builtin-strict <bool>    enforce strict built-in selection (default true)");
        output.WriteLine("  --dry-run                  report the planned transformation without writing");
        output.WriteLine("  --json                     emit JSON instead of text");
        output.WriteLine();
        output.WriteLine("inspect options:");
        output.WriteLine("  <assembly|dir>...          assemblies or directories to inspect");
        output.WriteLine("  --config <path>            configuration file (to report the merged rule set)");
        output.WriteLine("  --rule-set <path>          rule-set document (repeatable)");
        output.WriteLine("  --builtin <id|all>         built-in rule set (repeatable)");
        output.WriteLine("  --builtin-include <family> include built-in family (repeatable)");
        output.WriteLine("  --builtin-exclude <family> exclude built-in family (repeatable)");
        output.WriteLine("  --builtin-strict <bool>    enforce strict built-in selection (default true)");
        output.WriteLine("  --include <glob>           configuration include pattern (repeatable)");
        output.WriteLine("  --exclude <glob>           configuration exclude pattern (repeatable)");
        output.WriteLine("  --mode <Controlled|RaceExploration> configuration instrumentation granularity");
        output.WriteLine("  --r2r <Reject|StripToIL>   configuration ReadyToRun policy");
        output.WriteLine("  --strong-name <Fail|ReSign> legacy configuration option");
        output.WriteLine("  --strong-name-key <path>   legacy configuration option");
        output.WriteLine("  --exclude-framework <bool> configuration framework exclusion");
        output.WriteLine("  --instrument-dependencies <bool> configuration dependency instrumentation");
        output.WriteLine("  --target-runtime <version> configuration target runtime");
        output.WriteLine("  --json                     emit JSON instead of text");
        output.WriteLine();
        output.WriteLine();
        output.WriteLine("replay/exploration options:");
        output.WriteLine("  --assembly <path>          explicit scenario harness assembly");
        output.WriteLine("  --scenario-type <type>     public IReplayScenario implementation with a public parameterless constructor");
        output.WriteLine("  --manifest <path>          closure instrumentation manifest used for compatibility checks");
        output.WriteLine("  --seed <int>               stable model/application root seed");
        output.WriteLine("  --schedule-seed <int>      explicit scheduler seed");
        output.WriteLine("  --strategy <name>          fifo|round-robin|priority|seeded-random");
        output.WriteLine("  --max-steps <int>          controlled step bound per execution");
        output.WriteLine("  --json                     emit deterministic JSON");
        output.WriteLine();
        output.WriteLine("Exit codes: 0 success, 1 usage, 2 configuration, 3 closure, 4 instrumentation, 5 I/O,");
        output.WriteLine("            6 scenario failure, 7 replay/compatibility/divergence, 8 minimization.");
    }
}
