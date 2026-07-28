using System.Runtime.CompilerServices;

// The Clockwork assembly (`src/Clockwork/Clockwork.csproj`, packaged as Clockwork.Simulation) is
// the "simulation host": it is the only production code allowed to activate a simulation via the
// internal capability token in SimulationRuntimeActivation. See SimulationActivationToken for why
// this is internal rather than a public global boolean/environment variable.
[assembly: InternalsVisibleTo("Clockwork")]
[assembly: InternalsVisibleTo("Clockwork.Runtime.Tests")]

// The Clockwork test project is allowed to mint activation tokens so it can exercise the
// opt-in controlled-operation compatibility bridge in SimulationTaskQueue directly (which
// requires a real ambient-context configuration) without routing through a full cluster.
[assembly: InternalsVisibleTo("Clockwork.Tests")]
