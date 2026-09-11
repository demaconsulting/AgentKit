using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Composes tool packs into the tool list an application offers a model, registering only
///     those packs the host can actually support.
/// </summary>
/// <remarks>
///     <para>
///     <b>A pack the host cannot support contributes no tools at all.</b> It is not registered
///     and then refused on use: the model never sees the tool, so it cannot waste a turn on it
///     or rationalize around a refusal.
///     </para>
///     <para>
///     <b>Composition-time programming errors throw.</b> Only runtime policy decisions are
///     returned as results. A colliding family prefix, a pack that publishes a tool outside its
///     declared family, or a missing access policy are all mistakes in the composing
///     application's code, discovered by the developer who wrote them.
///     </para>
///     <para>
///     Instances are mutable and are not safe for concurrent use. A builder is a short-lived
///     object assembled on one thread during application start-up.
///     </para>
/// </remarks>
public sealed class ToolPackBuilder
{
    /// <summary>
    ///     The access policy handed to every pack asked to create its tools.
    /// </summary>
    private readonly PathPolicy _policy;

    /// <summary>
    ///     The packs added so far, in the order they were added.
    /// </summary>
    private readonly List<IToolPack> _packs = [];

    /// <summary>
    ///     The capabilities the host has declared.
    /// </summary>
    private HostCapabilities _hostCapabilities = HostCapabilities.None;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolPackBuilder"/> class.
    /// </summary>
    /// <remarks>
    ///     There is no constructor that omits the policy. A tool cannot be constructed without
    ///     its policy, so a builder cannot exist without one either.
    /// </remarks>
    /// <param name="policy">The access policy every tool will observe. Must not be null.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public ToolPackBuilder(PathPolicy policy)
    {
        // An unguarded builder would produce unguarded tools, so the policy is mandatory.
        ArgumentNullException.ThrowIfNull(policy);

        _policy = policy;
    }

    /// <summary>
    ///     Declares the capabilities the host provides.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A later call <b>replaces</b> the declaration rather than adding to it. A declaration
    ///     states what the host is, and a host has one answer; combining successive calls would
    ///     make the declaration a one-way ratchet that no caller could narrow.
    ///     </para>
    ///     <para>
    ///     The declaration applies at <see cref="Build"/>, not at <see cref="Add"/>, so the
    ///     order of the two calls does not matter.
    ///     </para>
    /// </remarks>
    /// <param name="capabilities">The capabilities the host provides.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    public ToolPackBuilder WithHostCapabilities(HostCapabilities capabilities)
    {
        _hostCapabilities = capabilities;
        return this;
    }

    /// <summary>
    ///     Adds a pack to the composition.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The family prefix is checked here rather than at <see cref="Build"/> so that the
    ///     collision is reported at the call the developer wrote, naming the pack that caused
    ///     it. Comparison is ordinal, because that is how every model provider compares the
    ///     tool names the prefix ends up in.
    ///     </para>
    ///     <para>
    ///     Adding the same pack instance twice is a collision like any other. It would publish
    ///     every one of that pack's tools twice.
    ///     </para>
    /// </remarks>
    /// <param name="pack">The pack to add. Must not be null and must carry a family prefix.</param>
    /// <returns>This builder, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="pack"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the pack's family prefix is empty, or when another pack already added
    ///     claims the same family prefix.
    /// </exception>
    public ToolPackBuilder Add(IToolPack pack)
    {
        // A missing pack is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(pack);

        // A pack with no prefix cannot own any name, so its tools could not be attributed to it.
        ArgumentException.ThrowIfNullOrEmpty(pack.FamilyPrefix);

        // Two packs claiming one prefix publish ambiguous tool names. Detecting it here rather
        // than at build time reports the mistake at the line that made it.
        if (_packs.Any(added => string.Equals(added.FamilyPrefix, pack.FamilyPrefix, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Another pack already claims this family prefix, and two packs sharing a prefix "
                + "would publish tools the model cannot tell apart.",
                nameof(pack));
        }

        _packs.Add(pack);
        return this;
    }

    /// <summary>
    ///     Creates the tools of every pack the host can support.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A pack is supported when every capability it requires is one the host declared;
    ///     a pack requiring <see cref="HostCapabilities.None"/> is therefore always supported.
    ///     An unsupported pack is never asked to create its tools at all.
    ///     </para>
    ///     <para>
    ///     Tools appear in the order the packs were added, and within a pack in the order it
    ///     produced them. The order a model sees is observable, so it is the caller's stated
    ///     order rather than an incidental one.
    ///     </para>
    ///     <para>
    ///     This method neither resets nor caches. Calling it twice reports the builder's state
    ///     as it is at each call, and creates a fresh set of tools each time.
    ///     </para>
    /// </remarks>
    /// <returns>The composed tools.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when a pack returns no tool collection, returns a null tool, or returns a tool
    ///     whose name does not carry the pack's declared family prefix.
    /// </exception>
    public IReadOnlyList<AIFunction> Build()
    {
        var tools = new List<AIFunction>();

        foreach (var pack in _packs)
        {
            // Gate first. An unsupported pack is not asked for its tools, so it cannot register
            // one by accident and cannot pay the cost of building tools nobody will see.
            var required = pack.RequiredCapabilities;
            if ((_hostCapabilities & required) != required)
            {
                continue;
            }

            AddToolsOf(pack, tools);
        }

        return tools;
    }

    /// <summary>
    ///     Appends one pack's tools to the composed list, checking each as it goes.
    /// </summary>
    /// <param name="pack">The pack to create tools from.</param>
    /// <param name="tools">The list being composed.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the pack returns no collection, a null tool, or a tool outside its
    ///     declared family.
    /// </exception>
    private void AddToolsOf(IToolPack pack, List<AIFunction> tools)
    {
        // A pack from another package may have been compiled without nullable annotations, so
        // the contract is enforced rather than assumed.
        var created = pack.CreateTools(_policy)
            ?? throw new InvalidOperationException(
                "The pack returned no tool collection.");

        // Every name must begin with the prefix the pack declared. The prefix check at Add time
        // is only meaningful if the declaration is true, and nothing else checks that: the
        // guarded factory validates that a name is well formed, not that it belongs to the pack
        // publishing it.
        var expected = pack.FamilyPrefix + "_";

        foreach (var tool in created)
        {
            if (tool is null)
            {
                throw new InvalidOperationException(
                    "The pack returned a null tool.");
            }

            if (!tool.Name.StartsWith(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The pack returned a tool whose name does not carry the family prefix the "
                    + "pack declared, so the prefix collision check cannot protect it.");
            }

            tools.Add(tool);
        }
    }
}

