using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The capabilities a host may or may not be able to provide to the tools it attaches.
/// </summary>
/// <remarks>
///     <para>
///     A capability is a property of the <em>host</em> — the application and the model behind
///     it — not of the machine. Whether a model can accept image content is the host's to
///     declare, and no library can discover it.
///     </para>
///     <para>
///     <see cref="None"/> is a real, useful value rather than a placeholder for "unset". A pack
///     whose <see cref="IToolPack.RequiredCapabilities"/> is <see cref="None"/> requires nothing
///     of its host and is therefore registered by every host, which is the natural expression of
///     "always available".
///     </para>
///     <para>
///     Members are added only when a pack needs one. A speculative capability is worse than no
///     capability: it is public API that must be honored forever, and a host that declares it
///     without understanding it silently widens what the model is offered.
///     </para>
/// </remarks>
[Flags]
public enum HostCapabilities
{
    /// <summary>
    ///     No capability is required or declared.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The host can accept image content in a tool result and present it to the model.
    /// </summary>
    Vision = 1
}

/// <summary>
///     A group of related tools published under one family prefix.
/// </summary>
/// <remarks>
///     <para>
///     A pack is what a package exposes instead of individual tools, so that adding a package to
///     an application costs one line rather than one line per tool.
///     </para>
///     <para>
///     A pack declares what its tools need of the host rather than deciding for itself whether
///     it can operate. The decision belongs to <see cref="ToolPackBuilder"/>, because only the
///     builder knows what the host declared, and because a pack that refused at call time would
///     already have been offered to the model.
///     </para>
///     <para>
///     Implementations are expected to be stateless and safe for concurrent use;
///     <see cref="CreateTools"/> receives everything it needs as an argument.
///     </para>
/// </remarks>
public interface IToolPack
{
    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     The prefix is the pack's identity: two packs claiming the same prefix would publish
    ///     tools whose names are ambiguous, which <see cref="ToolPackBuilder.Add"/> refuses.
    ///     Its value must be non-null and non-empty, and must be the leading portion of every
    ///     name <see cref="CreateTools"/> returns.
    /// </remarks>
    string FamilyPrefix { get; }

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.None"/> means the pack is always registered.
    /// </remarks>
    HostCapabilities RequiredCapabilities { get; }

    /// <summary>
    ///     Creates this pack's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     Called once per <see cref="ToolPackBuilder.Build"/>, and not called at all when the
    ///     host does not provide <see cref="RequiredCapabilities"/>.
    /// </remarks>
    /// <param name="policy">The access policy every returned tool must observe.</param>
    /// <returns>
    ///     The pack's tools. Must not be <see langword="null"/>, and must not contain a
    ///     <see langword="null"/> element.
    /// </returns>
    IEnumerable<AIFunction> CreateTools(PathPolicy policy);
}
