using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     The clock tool family: an author-written pack publishing the <c>clock_now</c> tool under the
///     <c>clock</c> family prefix.
/// </summary>
/// <remarks>
///     <para>
///     This pack demonstrates that the pack contract is uniform across tools of every kind. Its
///     tool takes no path and consults no policy, yet the pack is written exactly as a path-taking
///     one is: it declares a family prefix, requires no host capability, and returns its tool from
///     <see cref="CreateTools"/>. <see cref="ToolPackBuilder.Build"/> verifies the tool's name
///     begins with the prefix just the same.
///     </para>
///     <para>
///     <b>The policy argument to <see cref="CreateTools"/> is accepted and ignored</b> — the
///     deliberate contrast. The contract hands every pack the policy, but a pack whose tools govern
///     nothing a policy constrains is free not to consult it. That is the honest expression of a
///     no-path tool: not a policy that permits everything, but a tool that has nothing to ask a
///     policy about.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public sealed class ClockToolPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "clock";

    /// <summary>
    ///     Initializes a new instance of the <see cref="ClockToolPack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state; its tool needs nothing from a policy or the host.
    /// </remarks>
    public ClockToolPack()
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
    ///     <see cref="HostCapabilities.None"/>: reporting the time asks nothing of the host, so the
    ///     family is registered by every composition.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, ignoring the supplied policy.
    /// </summary>
    /// <remarks>
    ///     The <paramref name="policy"/> is part of the contract every pack satisfies, but this
    ///     pack's tool touches nothing a policy governs, so it is deliberately not consulted. This
    ///     is the point of the pack: it shows a no-path tool is built through the same guarded
    ///     factory and published through the same pack contract as any other, without pretending to
    ///     observe a policy it has no use for.
    /// </remarks>
    /// <param name="policy">The access policy, accepted to satisfy the contract and not consulted.</param>
    /// <returns>The single <c>clock_now</c> tool.</returns>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // The policy is intentionally unused: this pack's tool consults none. Referencing it here
        // would be theatre, so it is left alone rather than passed to a factory that ignores it.
        _ = policy;

        return [ClockNowTool.Create()];
    }
}
