namespace Clockwork.Runtime.Random;

/// <summary>
/// <para>
/// A root deterministic seed/decision service: derives independent, named-domain seeds (see
/// <see cref="SimulationSeedDomain"/>) from one root seed, and derives per-node/per-site child
/// seeds within a domain from a <em>stable string identity</em> rather than from mutable
/// construction/fork order.
/// </para>
/// <para>
/// <b>Why not just keep calling <c>Random.Fork()</c>/<c>Random.Next()</c>?</b> Forking a shared
/// <see cref="System.Random"/> in registration order (as e.g. <c>SimulationCluster{TNode}.CreateDerivedRandom()</c>
/// does today) makes every derived seed depend on the exact order and count of every earlier
/// derivation - reordering two <c>AddNode</c> calls, or adding an unrelated derivation in between,
/// silently reseeds every node that comes after it. <see cref="GetSiteSeed(SimulationSeedDomain, string)"/>
/// instead hashes <c>(RootSeed, domain, stableId)</c> directly: the result depends only on the
/// domain and the caller-supplied stable identity (e.g. a node's network address), not on when or
/// how many times it - or any other id - was derived. This is "stable per-node/per-site
/// derivation" as required by the roadmap: renaming, reordering, or adding unrelated nodes never
/// perturbs an existing node's seed.
/// </para>
/// <para>
/// <b>Domain independence.</b> <see cref="GetDomainSeed(SimulationSeedDomain)"/> hashes
/// <c>(RootSeed, domain)</c>, so each domain's seed is an independent function of the root seed and
/// that domain's name alone - consuming randomness from the resulting stream (however many draws,
/// however many further forks) never touches any other domain's seed or stream, because domain
/// seeds are computed once, up front, from immutable inputs rather than being threaded through a
/// single shared generator.
/// </para>
/// <para>
/// This service deals only in <see cref="int"/> seeds, not <see cref="System.Random"/> instances:
/// callers (e.g. the Clockwork.Simulation package) wrap a returned seed in whatever random
/// type is appropriate for their layer (<c>SimulationRandom</c> today).
/// </para>
/// </summary>
public sealed class SimulationSeedAuthority
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SimulationSeedAuthority"/> class.
    /// </summary>
    /// <param name="rootSeed">The root deterministic seed all domain/site seeds derive from.</param>
    public SimulationSeedAuthority(int rootSeed)
    {
        RootSeed = rootSeed;
    }

    /// <summary>
    /// Gets the root deterministic seed this authority was created with.
    /// </summary>
    public int RootSeed { get; }

    /// <summary>
    /// Derives the seed for an entire named domain. Stable across processes: the same
    /// <see cref="RootSeed"/> and <paramref name="domain"/> always produce the same result.
    /// </summary>
    /// <param name="domain">The domain to derive a seed for.</param>
    /// <returns>A deterministic seed, independent of every other domain's seed.</returns>
    public int GetDomainSeed(SimulationSeedDomain domain) =>
        DeterministicHash.ToInt32(RootSeedComponent, domain.ToString());

    /// <summary>
    /// Derives a per-node/per-site child seed within a domain, keyed by a caller-supplied stable
    /// identity (e.g. a node's network address, or a stable call-site tag) rather than by
    /// construction/fork order. See this type's remarks for why order-independence matters.
    /// </summary>
    /// <param name="domain">The domain the site belongs to.</param>
    /// <param name="siteId">
    /// A stable identity for the site (e.g. a node's network address). Must be non-empty and
    /// should be stable across runs for the same conceptual node/site.
    /// </param>
    /// <returns>
    /// A deterministic seed depending only on <see cref="RootSeed"/>, <paramref name="domain"/>,
    /// and <paramref name="siteId"/> - never on registration order or on any other site's identity.
    /// </returns>
    public int GetSiteSeed(SimulationSeedDomain domain, string siteId)
    {
        ArgumentException.ThrowIfNullOrEmpty(siteId);
        return DeterministicHash.ToInt32(RootSeedComponent, domain.ToString(), siteId);
    }

    /// <summary>
    /// Creates an independent child <see cref="SimulationSeedAuthority"/> for a stable identity
    /// (e.g. one authority per node), whose own domain/site seeds are derived from a root seed that
    /// depends only on this authority's <see cref="RootSeed"/> and <paramref name="stableId"/> -
    /// not on how many other child authorities have been created, or in what order.
    /// </summary>
    /// <param name="stableId">A stable identity for the child authority.</param>
    /// <returns>A new, independent <see cref="SimulationSeedAuthority"/>.</returns>
    public SimulationSeedAuthority CreateChildAuthority(string stableId)
    {
        ArgumentException.ThrowIfNullOrEmpty(stableId);
        return new SimulationSeedAuthority(DeterministicHash.ToInt32(RootSeedComponent, "Child", stableId));
    }

    private string RootSeedComponent => RootSeed.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
}
