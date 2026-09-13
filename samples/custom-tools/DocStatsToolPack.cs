using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     The document-statistics tool family: an author-written pack publishing the
///     <c>docstats_wordcount</c> tool under the <c>docstats</c> family prefix.
/// </summary>
/// <remarks>
///     <para>
///     This pack is the sample's demonstration that <b>publishing a pack is how a third party adds
///     tools</b>, exactly as the shipped packages do. It declares one family prefix, and
///     <see cref="ToolPackBuilder.Build"/> verifies that every tool it creates begins with that
///     prefix — which is why a tool from a different family could not be smuggled into this pack.
///     </para>
///     <para>
///     <b>The family prefix is deliberately not one the library ships.</b> The library owns the
///     <c>markdown</c> family, so a custom pack that claimed <c>markdown</c> would collide with it
///     at composition. The <c>docstats</c> prefix, and a word-count tool the library does not
///     provide, keep the sample teaching the extensibility contract without competing with a shipped
///     tool.
///     </para>
///     <para>
///     It requires no host capability, so every composition registers it. Its single tool is
///     governed entirely by the policy the <see cref="ToolPackBuilder"/> supplies; the pack itself
///     grants nothing.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public sealed class DocStatsToolPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "docstats";

    /// <summary>
    ///     Initializes a new instance of the <see cref="DocStatsToolPack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tool comes from the <see cref="ToolPackBuilder"/> the pack is added to.
    /// </remarks>
    public DocStatsToolPack()
    {
    }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so the constant above can keep the name the contract uses. The
    ///     class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.None"/>: counting a file's words asks nothing of the host, so
    ///     the family is registered by every composition.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     The policy is passed to the tool's factory and captured there, so the tool cannot later
    ///     observe a different policy.
    /// </remarks>
    /// <param name="policy">The access policy the returned tool observes.</param>
    /// <returns>The single <c>docstats_wordcount</c> tool.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to the tool's factory so the failure names the composing
        // application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        return [DocStatsWordCountTool.Create(policy)];
    }
}
