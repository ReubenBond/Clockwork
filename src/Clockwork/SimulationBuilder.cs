namespace Clockwork;

/// <summary>
/// A queued-up node registration produced by <see cref="SimulationBuilder"/>, materialized into a
/// real <see cref="SimulationNode"/> once <see cref="SimulationBuilder.Build"/> constructs the
/// enclosing <see cref="BuiltSimulation"/> and its shared clock/guard/queue/random exist.
/// </summary>
/// <param name="Address">The node's network address, used for uniqueness checks and diagnostics.</param>
/// <param name="Materialize">Creates the real node given its freshly-constructed context.</param>
internal sealed record SimulationBuilderPendingNode(string Address, Func<SimulationNodeContext, SimulationNode> Materialize);

/// <summary>
/// <para>
/// Fluent composition API for building a working <see cref="SimulationCluster{TNode}"/> without
/// hand-writing a trivial <see cref="SimulationCluster{TNode}"/>/<see cref="SimulationNode"/>
/// subclass for common cases. <see cref="Build"/> produces a <see cref="BuiltSimulation"/> - a
/// small, sealed <see cref="SimulationCluster{TNode}"/> over the common <see cref="SimulationNode"/>
/// base type - wired up with a deterministic seed, clock, per-node queues, and an auto-created
/// <see cref="SimulationNetwork"/>.
/// </para>
/// <para>
/// <see cref="AddNode{TState}(string, TState)"/> and its overloads return a ready-to-capture
/// <see cref="SimulationNodeHandle{TState}"/> for simulations that just need a named endpoint and
/// some application state - no subclass required. For simulations that still need custom behavior
/// (e.g. overriding methods, holding multiple pieces of typed state, or participating in
/// application-specific protocols), <see cref="AddCustomNode{TNode}(string, Func{SimulationNodeContext, TNode})"/>
/// registers any existing <see cref="SimulationNode"/> subclass side-by-side with plain handles in
/// the same cluster - this is a deliberately small, additive layer on top of
/// <see cref="SimulationCluster{TNode}"/>, not a replacement for it: hand-written
/// <see cref="SimulationCluster{TNode}"/> subclasses continue to work exactly as before and are
/// unaffected by this type's existence.
/// </para>
/// <para>
/// <b>Heterogeneous node foundation and its limits:</b> because <see cref="BuiltSimulation"/> is a
/// <see cref="SimulationCluster{TNode}"/> over the <see cref="SimulationNode"/> base type, plain
/// handles and arbitrary custom subclasses can be registered together and share the same clock,
/// guard, and drive loop - this is the foundation this PR ships. What is <em>not</em> included yet:
/// there is no dependency-injection-style construction or startup ordering between nodes, no
/// per-node-type discovery beyond <see cref="BuiltSimulation.GetNodeByAddress(string)"/>/<c>Nodes.OfType&lt;T&gt;()</c>,
/// and custom nodes registered via <see cref="AddCustomNode{TNode}(string, Func{SimulationNodeContext, TNode})"/>
/// are not returned synchronously (they can only be retrieved after <see cref="Build"/> - see that
/// method's remarks). Full heterogeneous lifecycle support is deferred to a future phase.
/// </para>
/// </summary>
public sealed class SimulationBuilder
{
    private readonly List<SimulationBuilderPendingNode> _pendingNodes = [];
    private readonly HashSet<string> _addresses = new(StringComparer.Ordinal);
    private int? _seed;
    private DateTimeOffset? _startDateTime;
    private CancellationToken _cancellationToken;
    private TimeZoneInfo? _simulationTimeZone;
    private int _buildStarted;
    private Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy _cryptoRandomnessPolicy =
        Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.Reject;

    /// <summary>
    /// Sets the seed used for the built simulation's deterministic random number generation. This
    /// must be called at least once before <see cref="Build"/> - there is no implicit "random"
    /// default, since that would make the simulation's behavior depend on wall-clock time or
    /// process state instead of being fully deterministic.
    /// </summary>
    /// <param name="seed">The seed for deterministic random number generation.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder WithSeed(int seed)
    {
        _seed = seed;
        return this;
    }

    /// <summary>
    /// Sets the starting date/time for the built simulation. Defaults to <see cref="DateTimeOffset.UtcNow"/>
    /// (captured when <see cref="Build"/> is called) if never set.
    /// </summary>
    /// <param name="startDateTime">The starting date/time for the simulation.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder WithStartDateTime(DateTimeOffset startDateTime)
    {
        _startDateTime = startDateTime;
        return this;
    }

    /// <summary>
    /// Sets a cancellation token to link with the built simulation's teardown cancellation.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to link with cluster teardown.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder WithCancellationToken(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        return this;
    }

    /// <summary>
    /// Sets the local time zone the deterministic <c>DateTime.Now</c>/<c>DateTime.Today</c> shims
    /// observe. Defaults to <see cref="TimeZoneInfo.Utc"/>.
    /// </summary>
    /// <param name="timeZone">The local time zone simulated nodes observe.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder WithSimulationTimeZone(TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        _simulationTimeZone = timeZone;
        return this;
    }

    /// <summary>
    /// Sets the cryptographic-randomness policy the deterministic crypto shims enforce. Defaults to
    /// <see cref="Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.Reject"/>. Choosing
    /// <see cref="Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy.DeterministicInsecureForTesting"/>
    /// substitutes deterministic <b>non-cryptographic</b> bytes and must only ever be used in tests -
    /// never a production security decision.
    /// </summary>
    /// <param name="policy">The cryptographic-randomness policy for the built simulation.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder WithCryptoRandomnessPolicy(Clockwork.Runtime.Shims.SimulationCryptoRandomnessPolicy policy)
    {
        _cryptoRandomnessPolicy = policy;
        return this;
    }

    /// <summary>
    /// Registers a plain node with no meaningful application state - just a named endpoint with its
    /// own queue, scheduler, and synchronization context. Equivalent to
    /// <c>AddNode&lt;object?&gt;(address, state: null)</c>.
    /// </summary>
    /// <param name="address">The node's unique network address.</param>
    /// <returns>A handle for the node, usable after <see cref="Build"/> is called.</returns>
    public SimulationNodeHandle<object?> AddNode(string address) => AddNode<object?>(address, static _ => null);

    /// <summary>
    /// Registers a node carrying the given application-defined state payload.
    /// </summary>
    /// <typeparam name="TState">The type of the state payload.</typeparam>
    /// <param name="address">The node's unique network address.</param>
    /// <param name="state">The state payload to associate with this node.</param>
    /// <returns>A handle for the node, usable after <see cref="Build"/> is called.</returns>
    public SimulationNodeHandle<TState> AddNode<TState>(string address, TState state) => AddNode(address, _ => state);

    /// <summary>
    /// Registers a node whose application-defined state payload is created from the node's freshly
    /// constructed <see cref="SimulationNodeContext"/> (e.g. so it can schedule work on the node's
    /// own queue or use the node's derived random generator at construction time).
    /// </summary>
    /// <typeparam name="TState">The type of the state payload.</typeparam>
    /// <param name="address">The node's unique network address.</param>
    /// <param name="stateFactory">Creates the state payload given the node's context.</param>
    /// <returns>A handle for the node, usable after <see cref="Build"/> is called.</returns>
    public SimulationNodeHandle<TState> AddNode<TState>(string address, Func<SimulationNodeContext, TState> stateFactory)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentNullException.ThrowIfNull(stateFactory);
        EnsureAddressIsUnique(address);

        var handle = new SimulationNodeHandle<TState>(address);
        _pendingNodes.Add(new SimulationBuilderPendingNode(address, context =>
        {
            handle.Attach(context, stateFactory(context));
            return handle;
        }));

        return handle;
    }

    /// <summary>
    /// <para>
    /// Registers an existing <see cref="SimulationNode"/> subclass, side-by-side with any plain
    /// handles added via the other <c>AddNode</c> overloads. This is the foundation for heterogeneous
    /// node composition described in this type's remarks.
    /// </para>
    /// <para>
    /// Unlike the handle-returning overloads, the constructed node cannot be handed back
    /// synchronously - <paramref name="factory"/> needs a real <see cref="SimulationNodeContext"/>,
    /// which does not exist until <see cref="Build"/> constructs the cluster. Retrieve the node
    /// afterwards via <see cref="BuiltSimulation.GetNodeByAddress(string)"/> or by filtering
    /// <see cref="SimulationCluster{TNode}.Nodes"/> with <c>OfType&lt;TNode&gt;()</c>.
    /// </para>
    /// </summary>
    /// <typeparam name="TNode">The concrete <see cref="SimulationNode"/> subclass to construct.</typeparam>
    /// <param name="address">The node's unique network address.</param>
    /// <param name="factory">Creates the node given its freshly-constructed context.</param>
    /// <returns>This builder, for chaining.</returns>
    public SimulationBuilder AddCustomNode<TNode>(string address, Func<SimulationNodeContext, TNode> factory)
        where TNode : SimulationNode
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureAddressIsUnique(address);

        _pendingNodes.Add(new SimulationBuilderPendingNode(address, context => factory(context)));
        return this;
    }

    /// <summary>
    /// Constructs the <see cref="BuiltSimulation"/>: creates the shared clock, guard, and random
    /// generator, then materializes every node queued up via <c>AddNode</c> (in registration order)
    /// and attaches each returned handle to its real context and state. A builder is single-use once
    /// materialization starts, including when materialization fails.
    /// </summary>
    /// <returns>The constructed simulation cluster.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="WithSeed"/> was never called or this builder has already started a build.
    /// </exception>
    public BuiltSimulation Build()
    {
        if (_seed is not { } seed)
        {
            throw new InvalidOperationException(
                "A seed must be specified via WithSeed(...) before calling Build(), so the resulting simulation is deterministic.");
        }

        if (Interlocked.Exchange(ref _buildStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "This SimulationBuilder has already started building a simulation and cannot be reused.");
        }

        return new BuiltSimulation(
            seed,
            _startDateTime,
            _pendingNodes,
            _cancellationToken,
            _simulationTimeZone,
            _cryptoRandomnessPolicy);
    }

    private void EnsureAddressIsUnique(string address)
    {
        if (!_addresses.Add(address))
        {
            throw new ArgumentException($"A node with address '{address}' has already been added to this builder.", nameof(address));
        }
    }
}
