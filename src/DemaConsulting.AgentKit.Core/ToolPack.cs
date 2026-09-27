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
    Vision = 1,

    /// <summary>
    ///     The host can create and run a further agent on the model provider it is configured for.
    /// </summary>
    /// <remarks>
    ///     Only the application knows its provider, its model and its credentials, so only the
    ///     application can start a second agent. A host that cannot — or will not — do so declines
    ///     to declare this, and a delegating family is then never registered rather than being
    ///     offered and refused on use.
    /// </remarks>
    Delegation = 2
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
///     <b>Which of its tools a pack publishes is the pack's own decision, and may depend on the
///     policy.</b> The two gates are distinct and complementary: the host capability decides
///     whether a pack is asked for tools at all, and the policy it is then handed decides which of
///     them it returns. See <see cref="CreateTools"/>.
///     </para>
///     <para>
///     Implementations are expected to be stateless and safe for concurrent use;
///     <see cref="CreateTools"/> receives everything it needs as an argument.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Implementing a pack an application can attach. It declares a family prefix — the leading
///     portion of every tool name it publishes, which <see cref="ToolPackBuilder.Build"/> verifies
///     — and the host capabilities its tools require. This pack's single tool takes no path, so it
///     requires nothing of the host and does not consult the policy it is handed.
///     </para>
///     <code>
///     public sealed class ClockToolPack : IToolPack
///     {
///         public string FamilyPrefix => "clock";
///
///         public HostCapabilities RequiredCapabilities => HostCapabilities.None;
///
///         public IEnumerable&lt;AIFunction&gt; CreateTools(PathPolicy policy)
///         {
///             // This pack's tool needs no policy, so it is accepted and ignored.
///             _ = policy;
///
///             var now = () => ToolResult.Structured(new { utcTime = DateTimeOffset.UtcNow.ToString("O") });
///             return [GuardedToolFactory.Create(now, "clock_now", "Reports the current UTC time.")];
///         }
///     }
///     </code>
/// </example>
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
    ///     <para>
    ///     Called once per <see cref="ToolPackBuilder.Build"/>, and not called at all when the
    ///     host does not provide <see cref="RequiredCapabilities"/>.
    ///     </para>
    ///     <para>
    ///     <b>A pack may return a subset of its family, chosen from the policy.</b> The contract is
    ///     that the collection is never null, holds no null element, and that every name it holds
    ///     carries <see cref="FamilyPrefix"/> — it is deliberately <em>not</em> that the same tools,
    ///     or the same number of them, are returned under every policy. An implementation is
    ///     encouraged to withhold a tool whose writes the policy governs when the policy permits no
    ///     writing anywhere, because a tool
    ///     whose only possible outcome is a refusal spends a declaration and the model's attention
    ///     to achieve nothing. <see cref="PathPolicy.AnyLocationIsWritable"/> exists for exactly
    ///     this decision: it answers, before the pack has any path to test, whether writing is
    ///     possible anywhere at all under this policy. There is no companion question about
    ///     reading, and none is needed: <see cref="PathRule"/> carries no write-only access level,
    ///     so a policy that permits writing somewhere permits reading there too, and a read tool
    ///     is unusable only under a policy granting nothing at all. A pack that withholds tools
    ///     should keep the
    ///     survivors in their usual relative order, so a model sees the family shortened rather
    ///     than rearranged.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every returned tool must observe.</param>
    /// <returns>
    ///     The pack's tools, which may be the subset of its family this policy leaves able to
    ///     write. Must not be <see langword="null"/>, and must not contain a
    ///     <see langword="null"/> element.
    /// </returns>
    IEnumerable<AIFunction> CreateTools(PathPolicy policy);
}
