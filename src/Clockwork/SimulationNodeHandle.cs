namespace Clockwork;

/// <summary>
/// Non-generic bridge that lets <see cref="BuiltSimulation"/>'s disposal logic reach a
/// <see cref="SimulationNodeHandle{TState}"/>'s state payload without knowing <c>TState</c>.
/// Internal implementation detail of the <see cref="SimulationBuilder"/> composition layer.
/// </summary>
internal interface ISimulationNodeStateHolder
{
    /// <summary>
    /// Gets the node's state payload as a plain <see cref="object"/>, or <see langword="null"/> if
    /// the handle has not been attached to a built simulation yet.
    /// </summary>
    object? StateObject { get; }

    /// <summary>
    /// Releases the context and state held by this node after failed materialization or disposal.
    /// </summary>
    void Detach();
}

/// <summary>
/// <para>
/// A ready-to-use <see cref="SimulationNode"/> for simulations built with <see cref="SimulationBuilder"/>,
/// carrying an application-defined <typeparamref name="TState"/> payload instead of requiring a
/// trivial hand-written <see cref="SimulationNode"/> subclass just to hold a queue and some state.
/// </para>
/// <para>
/// Instances are returned by <see cref="SimulationBuilder.AddNode{TState}(string, TState)"/> and its
/// overloads <em>before</em> <see cref="SimulationBuilder.Build"/> is called, so callers can capture
/// them in local variables for later use. However, <see cref="Context"/> and <see cref="State"/> are
/// only usable <em>after</em> <see cref="SimulationBuilder.Build"/> has run - accessing them earlier
/// throws <see cref="InvalidOperationException"/>, since the node's queue, clock, and random
/// generator do not exist until the enclosing <see cref="BuiltSimulation"/> has been constructed.
/// </para>
/// </summary>
/// <typeparam name="TState">The type of the application-defined state payload for this node.</typeparam>
public sealed class SimulationNodeHandle<TState> : SimulationNode, ISimulationNodeStateHolder
{
    private SimulationNodeContext? _context;
    private TState _state = default!;

    internal SimulationNodeHandle(string address)
    {
        NetworkAddress = address;
    }

    /// <inheritdoc />
    public override string NetworkAddress { get; }

    /// <inheritdoc />
    public override SimulationNodeContext Context => _context ?? throw NotAttached();

    /// <inheritdoc />
    public override bool IsInitialized => _context is not null;

    /// <summary>
    /// Gets the application-defined state payload for this node. Only usable after the enclosing
    /// <see cref="SimulationBuilder"/> has been built.
    /// </summary>
    public TState State => _context is not null ? _state : throw NotAttached();

    /// <inheritdoc />
    object? ISimulationNodeStateHolder.StateObject => _context is not null ? _state : null;

    void ISimulationNodeStateHolder.Detach()
    {
        _context = null;
        _state = default!;
    }

    /// <summary>
    /// Attaches this handle to a real node context and state payload. Called exactly once, by
    /// <see cref="BuiltSimulation"/>'s constructor, while materializing the nodes queued up on a
    /// <see cref="SimulationBuilder"/>.
    /// </summary>
    internal void Attach(SimulationNodeContext context, TState state)
    {
        if (_context is not null)
        {
            throw new InvalidOperationException($"Node '{NetworkAddress}' has already been attached to a built simulation.");
        }

        _context = context;
        _state = state;
    }

    private InvalidOperationException NotAttached() => new(
        $"Node '{NetworkAddress}' has not been built yet. SimulationNodeHandle<{typeof(TState).Name}> " +
        "instances returned by SimulationBuilder.AddNode are only usable after SimulationBuilder.Build() has been called.");
}
