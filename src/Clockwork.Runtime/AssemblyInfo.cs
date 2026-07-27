using System.Runtime.CompilerServices;

// The root Clockwork assembly (packaged as Clockwork.Simulation) is the "simulation host": it is
// the only production code allowed to activate a simulation via the internal capability token in
// SimulationRuntimeActivation. See SimulationActivationToken for why this is internal rather than
// a public global boolean/environment variable.
[assembly: InternalsVisibleTo("Clockwork")]
[assembly: InternalsVisibleTo("Clockwork.Runtime.Tests")]
