using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Agent;

/// <summary>
///     The agent tool family: the pack an application attaches to let an agent delegate a task to
///     another agent the application has registered.
/// </summary>
/// <remarks>
///     <para>
///     <b>Delegation is by name, from a list the application wrote.</b> The application registers
///     <see cref="AgentProfile"/> instances; the model picks one by name and states a task. It
///     never writes a child's instructions, never names a tool the application did not attach, and
///     never widens a grant. A parent agent that could author its own child's instructions could
///     grant that child behavior the application never sanctioned, and every control the
///     application configured would then be one prompt away from being reset.
///     </para>
///     <para>
///     <b>A child's tools are composed from the child's own state; a caller cannot hand a tool list
///     down.</b> This is the family's safety-critical property and it is enforced by API shape
///     rather than by care. Nothing public in this family accepts an <see cref="AIFunction"/>: this
///     pack takes <see cref="IToolPack"/> instances, and each delegated run composes them afresh
///     against the child's own policy. Per-composition state — a task list, a cut buffer — is
///     therefore allocated per child, and a sub-agent writing to its own list cannot reach its
///     parent's. Both of the natural ways to get this wrong (capturing the parent's store while
///     building the child's tools, and filtering a parent-bound tool list at the <c>agent_run</c>
///     call site) were made while this family was being spiked, and both routed a child's task list
///     into its parent's. Neither can now be written.
///     </para>
///     <para>
///     <b>The family requires the host to declare
///     <see cref="HostCapabilities.Delegation"/>.</b> Only the application knows its provider, its
///     model and its credentials, so only the application can start a second agent — the
///     runner the application supplies is that seam. A host that has not declared the capability
///     receives none of this family's tools: the composition never asks this pack to create them,
///     so <c>agent_run</c> is not refused at call time, it is never offered.
///     </para>
///     <para>
///     <b>Delegation is bounded by <see cref="ToolLimits.MaxAgentDepth"/>.</b> A delegated agent is
///     itself given this family when its profile admits <c>agent_run</c>, so chains are possible and
///     are budgeted: at the ceiling the tool is offered and refuses, stating the ceiling and the
///     current level.
///     </para>
///     <para>
///     The class is immutable after construction and is safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family. The application registers the profiles, lists the packs a child may
///     draw on, and supplies the runner that actually starts an agent on its own provider. Note
///     what is <em>not</em> here: nowhere does the application — or the parent agent — hand over a
///     built tool list.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(workspace, [PathRule.ReadOnly(workspace)]);
///
///     // The packs a delegated agent may draw on. The agent pack itself is never listed here.
///     IToolPack[] childPacks = [new TextFilePack(), new TodoPack()];
///
///     var reviewer = new AgentProfile(
///         name: "reviewer",
///         instructions: "You review source files and report findings. You do not modify anything.",
///         tools: ["text_file_read", "text_file_search"],
///         description: "Reads and searches files, and reports what it found.");
///
///     // The host starts the agent, because only the host knows its provider and credentials.
///     Func&lt;ChildAgentRequest, CancellationToken, Task&lt;string?&gt;&gt; runner =
///         (request, cancellationToken) =&gt;
///     {
///         // Build an agent on your own provider from request.Instructions and request.Tools,
///         // run it against request.Task, and return its final text.
///         return Task.FromResult&lt;string?&gt;("the child's report");
///     };
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .WithHostCapabilities(HostCapabilities.Delegation)
///         .Add(new TextFilePack())
///         .Add(new TodoPack())
///         .Add(new AgentPack([reviewer], runner, childPacks))
///         .Build();
///     </code>
/// </example>
public sealed class AgentPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "agent";

    /// <summary>
    ///     The profiles the application registered, in registration order.
    /// </summary>
    private readonly List<AgentProfile> _profiles;

    /// <summary>
    ///     The packs a delegated agent may draw its tools from.
    /// </summary>
    /// <remarks>
    ///     Packs, never tools. A pack can be asked for a fresh set of tools against a fresh policy;
    ///     a tool list can only be filtered, and filtering a parent's list is exactly the defect
    ///     this family exists to make unwritable.
    /// </remarks>
    private readonly List<IToolPack> _childPacks;

    /// <summary>
    ///     The host's means of starting an agent.
    /// </summary>
    private readonly Func<ChildAgentRequest, CancellationToken, Task<string?>> _runner;

    /// <summary>
    ///     The capabilities a delegated agent's host provides, applied when composing a child.
    /// </summary>
    private readonly HostCapabilities _hostCapabilities;

    /// <summary>
    ///     The depth of the agent this pack is being composed for, counting a root agent as zero.
    /// </summary>
    private readonly int _depth;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentPack"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b><paramref name="childPacks"/> takes packs, not tools, and that is the isolation
    ///     boundary.</b> A pack can be asked to create a fresh set of tools against a different
    ///     policy, which is what gives every delegated agent its own per-composition state. There is
    ///     no overload taking <see cref="AIFunction"/> instances, because the only tool list a caller
    ///     could pass is the parent's, and the only thing this family could do with it is filter it
    ///     — which is how a sub-agent's twelve-item checklist ends up replacing its parent's
    ///     sixty-eight-item list.
    ///     </para>
    ///     <para>
    ///     This pack must not appear in <paramref name="childPacks"/>. A delegated agent is given
    ///     this family automatically, one level deeper, whenever its profile admits
    ///     <c>agent_run</c>; listing it as well would publish the <c>agent</c> prefix twice.
    ///     </para>
    /// </remarks>
    /// <param name="profiles">
    ///     The agents the application is willing to have started, in the order the model should see
    ///     them. Must be non-null, must contain no null entry, and must carry no two profiles of the
    ///     same name. May be empty, which registers the tool with nothing to delegate to.
    /// </param>
    /// <param name="runner">
    ///     The host's means of starting an agent. Must be non-null; see
    ///     <see cref="ChildAgentRequest"/> for what a runner must do with it.
    /// </param>
    /// <param name="childPacks">
    ///     The packs a delegated agent may draw its tools from — normally the same packs the
    ///     application attached to the parent. Must be non-null, must contain no null entry, must
    ///     not contain an <see cref="AgentPack"/>, and must carry no two packs of the same family
    ///     prefix. May be empty, which registers profiles whose children have no tools.
    /// </param>
    /// <param name="hostCapabilities">
    ///     The capabilities the host provides to a delegated agent, applied when composing a child
    ///     exactly as <see cref="ToolPackBuilder.WithHostCapabilities"/> applies them to the parent.
    ///     Defaults to <see cref="HostCapabilities.Delegation"/> so that a child whose profile
    ///     admits <c>agent_run</c> can delegate in turn; a host whose children may also see, for
    ///     example, passes <c>HostCapabilities.Delegation | HostCapabilities.Vision</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="profiles"/>, <paramref name="runner"/> or
    ///     <paramref name="childPacks"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="profiles"/> holds a null entry or two profiles of the same
    ///     name, or when <paramref name="childPacks"/> holds a null entry, an
    ///     <see cref="AgentPack"/>, or two packs claiming one family prefix.
    /// </exception>
    public AgentPack(
        IEnumerable<AgentProfile> profiles,
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner,
        IEnumerable<IToolPack> childPacks,
        HostCapabilities hostCapabilities = HostCapabilities.Delegation)
        : this(profiles, runner, childPacks, hostCapabilities, depth: 0)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentPack"/> class at a stated depth.
    /// </summary>
    /// <remarks>
    ///     Private because depth is a budget the family keeps, not a value a caller chooses: a
    ///     public depth parameter would let a composing application reset the ceiling by starting a
    ///     nested agent at zero. The only caller is <see cref="CreateTools"/>, composing a child one
    ///     level deeper than itself.
    /// </remarks>
    /// <param name="profiles">The agents the application is willing to have started.</param>
    /// <param name="runner">The host's means of starting an agent.</param>
    /// <param name="childPacks">The packs a delegated agent may draw its tools from.</param>
    /// <param name="hostCapabilities">The capabilities the host provides to a delegated agent.</param>
    /// <param name="depth">The depth of the agent this pack is composed for.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="profiles"/>, <paramref name="runner"/> or
    ///     <paramref name="childPacks"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when a collection holds an entry the family could not compose from.
    /// </exception>
    private AgentPack(
        IEnumerable<AgentProfile> profiles,
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner,
        IEnumerable<IToolPack> childPacks,
        HostCapabilities hostCapabilities,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(childPacks);

        // Materialize once: a lazily evaluated sequence could yield a different set of profiles or
        // packs each time it was enumerated, which would make a child's composition unrepeatable.
        _profiles = [.. profiles];
        _childPacks = [.. childPacks];

        ValidateProfiles(_profiles);
        ValidateChildPacks(_childPacks);

        _runner = runner;
        _hostCapabilities = hostCapabilities;
        _depth = depth;
    }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so that the constant above can keep the name the contract
    ///     uses. The class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.Delegation"/>: the family starts a second agent, which only
    ///     the application can do. Declaring the requirement is what lets the composition withhold
    ///     the family from a host that will not delegate, rather than offering a tool whose every
    ///     use would fail.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.Delegation;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The policy is where a child's reach is settled. Every registered profile's grants are
    ///     checked against it <em>here</em>, at composition, rather than when a child is started:
    ///     a profile reaching outside what this composition holds is a mistake in the application's
    ///     own configuration, and it is reported to the developer who wrote it instead of becoming
    ///     a refusal a model has to work around at run time.
    ///     </para>
    ///     <para>
    ///     The child-composition seam is created here as a closure over this pack's immutable
    ///     state. It closes over no store, no buffer and no tool: every child is composed from the
    ///     registered packs against that child's own policy, so nothing this composition owns can
    ///     reach a child, and nothing a child owns can reach this composition.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy of the composing agent.</param>
    /// <returns>The <c>agent_run</c> tool.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when a registered profile's grants would widen this policy — a location it does
    ///     not reach, a write where it holds only a read, or a deny pattern it imposes and the
    ///     profile drops.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to the tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        // A widening profile is a configuration error, and this is the first moment both halves of
        // it are known. Checking every profile now means no run can widen later.
        foreach (var profile in _profiles)
        {
            ValidateNarrowing(policy, profile);
        }

        return
        [
            AgentRunTool.Create(policy, _profiles, _runner, profile => ComposeChild(policy, profile), _depth)
        ];
    }

    /// <summary>
    ///     Composes one child agent's tools, from that child's own policy and a fresh creation of
    ///     every registered pack.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Every tool here is new.</b> The packs are asked for tools again rather than reusing
    ///     the parent's, so each child gets its own per-composition state — its own task list, its
    ///     own cut buffer — and two sibling children get different ones. The parent's tools are not
    ///     consulted, filtered, or even in scope.
    ///     </para>
    ///     <para>
    ///     The composition runs through a real <see cref="ToolPackBuilder"/>, so a child's tools are
    ///     subject to the same family-prefix verification and host-capability gating as the parent's.
    ///     This pack is added one level deeper so that a profile admitting <c>agent_run</c> yields a
    ///     child that can delegate in turn, up to the depth ceiling.
    ///     </para>
    ///     <para>
    ///     The profile's declared names are then applied as a filter. A name nothing published
    ///     contributes nothing: a profile cannot conjure a tool the application never attached.
    ///     </para>
    ///     <para>
    ///     The pack added one level deeper carries only the profiles the child's own policy still
    ///     covers. A child holds a narrower policy by design, so a sibling profile it can no longer
    ///     reach is absent from it rather than a configuration error reported at run time.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The parent composition's policy, which the child's may only narrow.</param>
    /// <param name="profile">The profile whose grants and declared names shape the child.</param>
    /// <returns>The child's own tools, in composition order.</returns>
    private IReadOnlyList<AIFunction> ComposeChild(PathPolicy policy, AgentProfile profile)
    {
        var childPolicy = BuildChildPolicy(policy, profile);

        var builder = new ToolPackBuilder(childPolicy).WithHostCapabilities(_hostCapabilities);
        foreach (var pack in _childPacks)
        {
            builder.Add(pack);
        }

        // One level deeper, carrying only the registrations the child's own policy still covers: a
        // child whose profile admits agent_run can delegate in turn, and its own ceiling check sees
        // the greater depth. A sibling profile this child can no longer reach is not a configuration
        // error — the application's own composition already judged it — so it is simply absent, and
        // a model naming it gets the ordinary unknown-profile refusal.
        builder.Add(new AgentPack(Reachable(childPolicy), _runner, _childPacks, _hostCapabilities, _depth + 1));

        var composed = builder.Build();

        // The profile is a filter over what was published, never a source of tools.
        var admitted = new HashSet<string>(profile.Tools, StringComparer.Ordinal);
        return [.. composed.Where(tool => admitted.Contains(tool.Name))];
    }

    /// <summary>
    ///     Selects the registered profiles a child holding the supplied policy could still delegate
    ///     to.
    /// </summary>
    /// <remarks>
    ///     A child legitimately holds a narrower policy than its parent, so a sibling profile whose
    ///     grants that policy no longer covers is not a mistake anyone made: the application's own
    ///     composition already judged every profile against the policy the application configured.
    ///     Filtering here rather than validating only the delegated-to profile is what keeps the
    ///     property at every depth — a child carrying the full list would raise the same widening
    ///     failure the moment it composed a grandchild.
    /// </remarks>
    /// <param name="policy">The child's own policy.</param>
    /// <returns>The profiles that policy covers, in registration order.</returns>
    private List<AgentProfile> Reachable(PathPolicy policy)
    {
        return [.. _profiles.Where(profile => Narrows(policy, profile))];
    }

    /// <summary>
    ///     Builds the policy a child observes.
    /// </summary>
    /// <remarks>
    ///     A profile that states no grants yields the parent's policy unchanged, which is the
    ///     common case: most children differ from their parent in what they are told and which
    ///     tools they hold, not in where they may work. A profile that states grants has already
    ///     been checked by <see cref="ValidateNarrowing"/>, so the child's policy is necessarily
    ///     no wider. The working directory and the limits are inherited, because a child that
    ///     resolved relative paths differently from its parent would misread every path the parent
    ///     passed it in a task.
    /// </remarks>
    /// <param name="policy">The parent composition's policy.</param>
    /// <param name="profile">The profile whose grants narrow it, if any.</param>
    /// <returns>The child's policy.</returns>
    private static PathPolicy BuildChildPolicy(PathPolicy policy, AgentProfile profile)
    {
        return profile.Grants.Count == 0
            ? policy
            : new PathPolicy(policy.WorkingDirectory, profile.Grants, policy.Limits);
    }

    /// <summary>
    ///     Rejects a profile whose grants would give a child more reach than its parent has.
    /// </summary>
    /// <remarks>
    ///     Every grant the profile states must be covered by some grant the parent holds. Being
    ///     covered means three things at once: the location is one the parent can reach, the access
    ///     level is no higher than the parent's, and every deny pattern the covering parent grant
    ///     imposes is carried by the child's grant too. The third is easy to overlook and is the
    ///     one that matters most — a child grant over the same root with the parent's exclusions
    ///     dropped is a strictly wider grant wearing a narrower shape.
    /// </remarks>
    /// <param name="policy">The parent composition's policy.</param>
    /// <param name="profile">The profile to check.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when any grant the profile states is not covered by a grant the policy holds.
    /// </exception>
    private static void ValidateNarrowing(PathPolicy policy, AgentProfile profile)
    {
        if (!Narrows(policy, profile))
        {
            throw new InvalidOperationException(
                "The '" + profile.Name + "' profile states a grant that this composition's "
                + "policy does not cover, so delegating to it would give a child more reach "
                + "than the agent that delegates to it has. A profile's grants may only "
                + "narrow.");
        }
    }

    /// <summary>
    ///     Determines whether every grant a profile states is covered by a grant the policy holds.
    /// </summary>
    /// <remarks>
    ///     The single test both the parent-level rejection and the child-level filter are written
    ///     in terms of, so the two can never disagree about what "narrower" means. A profile stating
    ///     no grants narrows vacuously: it inherits the policy it is judged against.
    /// </remarks>
    /// <param name="policy">The policy the profile is judged against.</param>
    /// <param name="profile">The profile to judge.</param>
    /// <returns>
    ///     <see langword="true"/> when the policy covers every grant the profile states; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool Narrows(PathPolicy policy, AgentProfile profile)
    {
        return profile.Grants.All(grant => policy.Grants.Any(held => Covers(held, grant)));
    }

    /// <summary>
    ///     Determines whether one grant is wholly contained by another.
    /// </summary>
    /// <param name="held">The grant the parent policy holds.</param>
    /// <param name="wanted">The grant the profile states.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="held"/> covers <paramref name="wanted"/>;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    private static bool Covers(PathRule held, PathRule wanted)
    {
        // A write where the parent holds only a read is a widening, whatever the location.
        if (wanted.Access == AccessLevel.ReadWrite && held.Access != AccessLevel.ReadWrite)
        {
            return false;
        }

        // An unrestricted child grant is only covered by an unrestricted parent grant; a rooted
        // parent grant can never contain "everywhere".
        if (wanted.Root is null)
        {
            if (held.Root is not null)
            {
                return false;
            }
        }
        else if (!held.Allows(wanted.Root))
        {
            // Allows checks both containment and the parent's deny patterns, so a child rooted at a
            // location the parent excludes is refused here.
            return false;
        }

        // Every exclusion the parent imposes must survive in the child, or the child's grant is
        // wider than the parent's over the same ground.
        return held.DenyPatterns.All(
            pattern => wanted.DenyPatterns.Contains(pattern, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Rejects a profile collection a family could not be composed from.
    /// </summary>
    /// <remarks>
    ///     Two profiles of one name would make the model's selection ambiguous, and the tool would
    ///     silently always choose the first. Detecting it at construction reports the mistake at the
    ///     line that made it.
    /// </remarks>
    /// <param name="profiles">The profiles to validate.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when a profile is null or two profiles carry the same name.
    /// </exception>
    private static void ValidateProfiles(List<AgentProfile> profiles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (profile is null)
            {
                throw new ArgumentException(
                    "A profile in the collection is null.",
                    nameof(profiles));
            }

            if (!seen.Add(profile.Name))
            {
                throw new ArgumentException(
                    "Two profiles carry the name '" + profile.Name
                    + "'; a model selects a profile by name, so names must be unique.",
                    nameof(profiles));
            }
        }
    }

    /// <summary>
    ///     Rejects a child-pack collection a child could not be composed from.
    /// </summary>
    /// <remarks>
    ///     The prefix collision is the same rule <see cref="ToolPackBuilder.Add"/> applies, checked
    ///     here as well so that the mistake is reported when the application registers the packs
    ///     rather than the first time a model happens to delegate. This pack is excluded because the
    ///     family adds itself to every child composition, one level deeper.
    /// </remarks>
    /// <param name="childPacks">The packs to validate.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when a pack is null, is an <see cref="AgentPack"/>, or shares a family prefix with
    ///     another pack in the collection.
    /// </exception>
    private static void ValidateChildPacks(List<IToolPack> childPacks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in childPacks)
        {
            if (pack is null)
            {
                throw new ArgumentException(
                    "A pack in the collection is null.",
                    nameof(childPacks));
            }

            if (pack is AgentPack)
            {
                throw new ArgumentException(
                    "The agent pack must not be listed as a child pack; a delegated agent is given "
                    + "the family automatically, one level deeper, when its profile admits "
                    + AgentRunTool.ToolName + ".",
                    nameof(childPacks));
            }

            if (!seen.Add(pack.FamilyPrefix))
            {
                throw new ArgumentException(
                    "Two child packs claim the family prefix '" + pack.FamilyPrefix
                    + "', and a child composed from both would publish tools it cannot tell apart.",
                    nameof(childPacks));
            }
        }
    }
}
