using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     A tool pack that publishes named do-nothing tools under a stated prefix, and records the
///     policies it has been asked to create tools against.
/// </summary>
/// <remarks>
///     <para>
///     The agent family's job is to compose <em>other</em> packs for a child. Verifying that at the
///     unit level with a real family would make the agent unit tests depend on an unrelated
///     subsystem, so this stub stands in: it is the smallest thing that satisfies
///     <see cref="IToolPack"/> and lets a test assert which tools a child received and which policy
///     they were built against.
///     </para>
///     <para>
///     Each tool it publishes carries a fresh identity per <see cref="CreateTools"/> call, which is
///     what lets a test prove that a child's tools are newly composed rather than the parent's own
///     tools handed down.
///     </para>
///     <para>
///     Instances are not thread-safe and are not intended to be shared between tests.
///     </para>
/// </remarks>
internal sealed class StubToolPack : IToolPack
{
    /// <summary>
    ///     The unqualified verbs this pack publishes, in order.
    /// </summary>
    private readonly string[] _verbs;

    /// <summary>
    ///     The policies this pack has been asked to create tools against, in call order.
    /// </summary>
    private readonly List<PathPolicy> _policies = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="StubToolPack"/> class.
    /// </summary>
    /// <param name="familyPrefix">The family prefix every published name carries.</param>
    /// <param name="verbs">The unqualified verbs to publish under that prefix.</param>
    public StubToolPack(string familyPrefix, params string[] verbs)
    {
        FamilyPrefix = familyPrefix;
        _verbs = verbs;
    }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    public string FamilyPrefix { get; }

    /// <summary>
    ///     Gets the capabilities the host must provide, which for a stub is none.
    /// </summary>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Gets the policies this pack has been asked to create tools against, in call order.
    /// </summary>
    /// <remarks>
    ///     A child is composed against its own policy, so a test proving narrowing reads the second
    ///     entry here rather than inspecting the child's tools.
    /// </remarks>
    public IReadOnlyList<PathPolicy> Policies => _policies;

    /// <summary>
    ///     Creates one do-nothing tool per published verb, governed by the supplied policy.
    /// </summary>
    /// <param name="policy">The access policy of the composition, recorded for inspection.</param>
    /// <returns>The published tools, in the order the verbs were stated.</returns>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        _policies.Add(policy);

        return
        [
            .. _verbs.Select(verb => GuardedToolFactory.Create(
                () => ToolResult.Text(verb),
                ToolName.Create(FamilyPrefix, verb),
                "A stub tool that reports its own verb."))
        ];
    }
}
