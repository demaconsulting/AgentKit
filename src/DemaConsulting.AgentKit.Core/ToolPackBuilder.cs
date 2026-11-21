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
///     <b>A tool whose writes the policy has disabled is likewise never offered.</b> The same
///     reasoning
///     applies one level finer, and the two gates divide cleanly: the host capability decides
///     <em>whether a pack is asked for its tools</em>, and the policy the pack is handed decides
///     <em>which of its tools it returns</em>. A pack is free to withhold a tool that could only
///     ever answer a refusal under the policy in force — the built-in file families do exactly
///     that for their write-performing tools, using
///     <see cref="PathPolicy.AnyLocationIsWritable"/> — and the builder does not distinguish a
///     withheld tool from one the pack never had. It verifies whatever is returned against the
///     pack's declared prefix exactly as before.
///     </para>
///     <para>
///     <b>Composition-time programming errors throw.</b> Only runtime policy decisions are
///     returned as results. A colliding family prefix, a pack that publishes a tool outside its
///     declared family, or a missing access policy are all mistakes in the composing
///     application's code, discovered by the developer who wrote them.
///     </para>
///     <para>
///     <b>Retention of large outputs is a composition choice.</b>
///     <see cref="WithToolOutputSpill"/> asks the builder to wrap what it composes so an output
///     too large to return is retained and previewed rather than refused, and to append the two
///     reader tools that page through and search it. A builder that is not asked composes exactly
///     what it always did.
///     </para>
///     <para>
///     Instances are mutable and are not safe for concurrent use. A builder is a short-lived
///     object assembled on one thread during application start-up.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Composing a tool list: one policy governs every tool, the host declares what it can support,
///     and each pack is added in the order the model should see its tools. The shipped packs
///     <c>TextFilePack</c> and <c>ImagePack</c> (from the <c>DemaConsulting.AgentKit.Tools</c>
///     package) are the usual arguments; this snippet takes them as <see cref="IToolPack"/> so the
///     shape is clear without Core depending on Tools. Declaring
///     <see cref="HostCapabilities.Vision"/> is what allows a vision-requiring pack to contribute
///     its tools at all — withhold it and that pack is never even asked.
///     </para>
///     <code>
///     // Composed once at application start-up; the result is handed to an agent factory.
///     public IReadOnlyList&lt;AIFunction&gt; BuildTools(
///         PathPolicy policy,
///         IToolPack textFiles,
///         IToolPack images,
///         bool visionEnabled)
///     {
///         var builder = new ToolPackBuilder(policy).Add(textFiles);
///
///         if (visionEnabled)
///         {
///             builder = builder
///                 .WithHostCapabilities(HostCapabilities.Vision)
///                 .Add(images);
///         }
///
///         return builder.Build();
///     }
///     </code>
/// </example>
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
    ///     The retention options for large tool outputs, or <see langword="null"/> when the
    ///     composition does not retain them.
    /// </summary>
    private ToolOutputOptions? _toolOutput;

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
    ///     Composes retention of large tool outputs: an output too large to return is retained, the
    ///     model is handed a bounded preview and a reference, and the reader tools
    ///     <c>tool_output_read</c> and <c>tool_output_search</c> are appended to the composed list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Without this call the composition is exactly what it was: a tool whose output exceeds
    ///     <see cref="ToolLimits.MaxResultCharacters"/> refuses as it always did. With it, every
    ///     composed tool is wrapped as <see cref="ToolOutputSpill.Apply"/> describes, against the
    ///     builder's policy limits.
    ///     </para>
    ///     <para>
    ///     A later call <b>replaces</b> the options, as <see cref="WithHostCapabilities"/> does, and
    ///     the choice applies at <see cref="Build"/>, so it may be made before or after
    ///     <see cref="Add"/>. Each <see cref="Build"/> allocates a fresh store, so two built lists
    ///     never share references.
    ///     </para>
    ///     <para>
    ///     The <c>tool_output</c> name prefix is reserved while retention is composed: a pack
    ///     claiming it, or publishing a tool under it, is reported at <see cref="Build"/>. So is a
    ///     policy whose <see cref="ToolLimits.MaxResultCharacters"/> is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/>, too small for a preview naming its
    ///     reference to fit.
    ///     </para>
    /// </remarks>
    /// <param name="options">
    ///     The preview size and retention ceilings, or <see langword="null"/> for
    ///     <see cref="ToolOutputOptions.Default"/>.
    /// </param>
    /// <returns>This builder, so calls can be chained.</returns>
    public ToolPackBuilder WithToolOutputSpill(ToolOutputOptions? options = null)
    {
        _toolOutput = options ?? ToolOutputOptions.Default;
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
    ///     A supported pack returns whichever of its tools the policy leaves able to act,
    ///     which need not be its whole family and need not be the same set under every policy.
    ///     The builder takes what it is given and verifies it against the pack's declared prefix;
    ///     it does not know, and does not report, that a tool was withheld.
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
    ///     <para>
    ///     When <see cref="WithToolOutputSpill"/> was called, the composed tools are wrapped so
    ///     oversized outputs are retained, and the two reader tools are appended after every
    ///     pack's tools. Because a model selects a tool by its name, two composed tools of one name
    ///     are then rejected, as is a tool whose name is longer than <see cref="ToolName.MaxLength"/>
    ///     and a policy whose result ceiling is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/> — the ceiling before any pack is
    ///     asked for its tools. Without retention the composed tools are returned as the packs
    ///     produced them, and the agent factories refuse a list holding two tools of one name.
    ///     </para>
    /// </remarks>
    /// <returns>The composed tools.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when a pack returns no tool collection, returns a null tool, or returns a tool
    ///     whose name does not carry the pack's declared family prefix; or, when retention of large
    ///     outputs is composed, when the policy's result ceiling is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/>, when a pack claims the reserved
    ///     <c>tool_output</c> prefix or publishes a tool under it, when two composed tools share a
    ///     name, or when a composed tool's name is longer than <see cref="ToolName.MaxLength"/>.
    /// </exception>
    public IReadOnlyList<AIFunction> Build()
    {
        // A ceiling too small for a preview's header is a composition mistake, reported before any
        // pack is asked for tools. Checked only when retention is chosen, so a composition that
        // never opts in accepts every ceiling it always did.
        if (_toolOutput is not null && !ToolOutputSpill.AdmitsCeiling(_policy.Limits))
        {
            throw new InvalidOperationException(ToolOutputSpill.DescribeCeilingTooSmall(_policy.Limits));
        }

        // The reader prefix is reserved only once retention is chosen, so a composition that never
        // opts in behaves exactly as it did. Checked here rather than at Add because the choice may
        // be made after the packs are added.
        if (_toolOutput is not null
            && _packs.Any(pack => string.Equals(pack.FamilyPrefix, ToolOutputSpill.ReservedPrefix, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "A pack claims the '" + ToolOutputSpill.ReservedPrefix + "' family prefix, which is "
                + "reserved for the reader tools a composition retaining large outputs appends.");
        }

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

        if (_toolOutput is null)
        {
            return tools;
        }

        // A tool published under the reserved prefix by a pack with a shorter prefix (for example
        // 'tool') would be indistinguishable from the readers, so it is reported as the pack's
        // mistake rather than as an argument error from the composition below.
        var reserved = tools.Find(tool => ToolOutputSpill.IsReservedName(tool.Name));
        if (reserved is not null)
        {
            throw new InvalidOperationException(
                "A pack published the tool '" + reserved.Name + "', whose name carries the '"
                + ToolOutputSpill.ReservedPrefix + "' prefix reserved for the reader tools a "
                + "composition retaining large outputs appends.");
        }

        // Two tools of one name would make Apply raise an ArgumentException, which is not what this
        // method documents. A pack returning a name twice, or two packs with overlapping prefixes,
        // is a composition mistake, so it is reported like the others. Checked only here so that a
        // composition that never opts in behaves exactly as it did.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicate = tools.Find(tool => !seen.Add(tool.Name));
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                "The composition publishes the tool '" + duplicate.Name + "' twice, because a pack "
                + "returned it twice or two packs with overlapping prefixes both published it, and "
                + "a model selects a tool by its name.");
        }

        // A name longer than any provider accepts would make Apply raise an ArgumentException; it
        // can only come from a pack returning a hand-built function, so it is the pack's mistake.
        var overlong = tools.Find(tool => ToolOutputSpill.IsNameTooLong(tool.Name));
        if (overlong is not null)
        {
            throw new InvalidOperationException(ToolOutputSpill.DescribeNameTooLong(overlong.Name));
        }

        return ToolOutputSpill.Apply(tools, _policy.Limits, _toolOutput);
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

