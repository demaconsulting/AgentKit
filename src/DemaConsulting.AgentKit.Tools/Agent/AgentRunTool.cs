using System.ComponentModel;
using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Agent;

/// <summary>
///     Builds the tools of one child agent, from that child's own composition.
/// </summary>
/// <remarks>
///     <para>
///     <b>This delegate is internal, and that is the whole isolation mechanism.</b> No public API
///     accepts a tool list for a child, so there is no expression in which a caller hands down the
///     parent's tools to be filtered. The only implementation is
///     <see cref="AgentPack.CreateTools"/>'s, which composes the packs the application registered
///     against the child's policy — a fresh <c>CreateTools</c> call per child, and therefore fresh
///     per-composition state, including a fresh task list.
///     </para>
///     <para>
///     It is invoked once per <c>agent_run</c> call rather than once at composition, because two
///     sibling children must not share the state a single composition would have given them.
///     </para>
/// </remarks>
/// <param name="profile">The profile whose declared tool names filter the composition.</param>
/// <returns>The child's own tools, in composition order.</returns>
internal delegate IReadOnlyList<AIFunction> ChildToolComposer(AgentProfile profile);

/// <summary>
///     The <c>agent_run</c> tool: starts one of the application's registered agents on a task and
///     returns what it said.
/// </summary>
/// <remarks>
///     <para>
///     <b>The model chooses a child by name; it never describes one.</b> The tool takes a
///     <c>profile</c> and a <c>task</c> and nothing else. The child's instructions, tool set and
///     path grants all come from the <see cref="AgentProfile"/> the application registered, so a
///     parent agent cannot grant a child behavior the application never sanctioned. The task is the
///     one thing the parent supplies, and it is data the child works on rather than authority the
///     child carries.
///     </para>
///     <para>
///     <b>The child's tools are built from the child's own state, never filtered from the
///     parent's.</b> The composition runs through <see cref="ChildToolComposer"/>, an internal
///     seam whose only implementation re-composes the registered packs against the child's policy.
///     Filtering a parent-bound tool list at this call site is a mistake that was actually made
///     while this family was being spiked — it routed a child's task list straight into its
///     parent's — so the API is shaped so that the mistake cannot be written: nothing public here
///     accepts an <see cref="AIFunction"/>.
///     </para>
///     <para>
///     <b>Delegation is bounded by <see cref="ToolLimits.MaxAgentDepth"/>.</b> An agent that can
///     delegate can delegate to something that delegates, and the resulting chain spends the host's
///     money in a way no single agent's transcript reveals. The ceiling is checked before anything
///     is composed or started, and the refusal states the ceiling and the current level as facts.
///     </para>
///     <para>
///     <b>Refusals state facts and prescribe nothing.</b> An unknown profile names the profiles that
///     do exist — a statement about this tool's own state, like the text family's empty-buffer
///     refusal — and does not redirect the model to another tool.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures the immutable profiles, the host's runner, and the composer it was
///     built with.
///     </para>
/// </remarks>
public static class AgentRunTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "agent_run";

    /// <summary>
    ///     The fixed opening of the description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     The available profiles are appended to this at construction, because a model that cannot
    ///     see the names it may choose from will invent one and spend a turn being refused.
    /// </remarks>
    private const string ToolDescriptionPrefix =
        "Delegates a task to one of the agents this application provides, and returns what that "
        + "agent finally said. Name the agent with 'profile' and state what it should do with "
        + "'task'. Each agent's instructions and tools are fixed by the application; you supply "
        + "only the task. The agent keeps its own working state, including its own task list. "
        + "Available profiles: ";

    /// <summary>
    ///     Creates the <c>agent_run</c> tool.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment, and because two of
    ///     these parameters — the composer and the depth — are the family's isolation and budget
    ///     controls rather than things a caller should be choosing. An application obtains this tool
    ///     by attaching <see cref="AgentPack"/>.
    /// </remarks>
    /// <param name="policy">The parent composition's policy, whose limits bound delegation.</param>
    /// <param name="profiles">
    ///     The profiles the application registered, in registration order. Must be non-null.
    /// </param>
    /// <param name="runner">The host's means of starting an agent. Must be non-null.</param>
    /// <param name="composer">
    ///     The child-composition seam. Must be non-null; see <see cref="ChildToolComposer"/>.
    /// </param>
    /// <param name="depth">
    ///     The depth of the agent this tool is being built for, counting an application's root
    ///     agent as zero. Must not be negative.
    /// </param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/>, <paramref name="profiles"/>,
    ///     <paramref name="runner"/> or <paramref name="composer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="depth"/> is negative.
    /// </exception>
    internal static AIFunction Create(
        PathPolicy policy,
        IReadOnlyList<AgentProfile> profiles,
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner,
        ChildToolComposer composer,
        int depth)
    {
        // Every one of these is supplied by the family that owns this tool, so a missing one is a
        // defect in this library rather than something a composing application did.
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentOutOfRangeException.ThrowIfNegative(depth);

        // Declared to return Task<object> on purpose; see the remarks on GuardedToolFactory.
        var run = async (
                [Description(
                    "The name of the agent to delegate to, chosen from the profiles named in this "
                    + "tool's description.")]
                string? profile = null,
                [Description(
                    "What that agent should do, stated in full. The agent sees only this task and "
                    + "its own instructions, not your conversation.")]
                string? task = null,
                CancellationToken cancellationToken = default) =>
            await RunAsync(policy, profiles, runner, composer, depth, profile, task, cancellationToken)
                .ConfigureAwait(false);

        return GuardedToolFactory.Create(run, ToolName, DescribeTool(profiles));
    }

    /// <summary>
    ///     Starts a child agent, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is deliberate: the request is judged, then the profile is
    ///     resolved, then the budget, and only then is anything composed or started. Nothing is
    ///     built for a run that is going to be refused.
    /// </remarks>
    /// <param name="policy">The parent composition's policy, whose limits bound delegation.</param>
    /// <param name="profiles">The profiles the application registered.</param>
    /// <param name="runner">The host's means of starting an agent.</param>
    /// <param name="composer">The child-composition seam.</param>
    /// <param name="depth">The depth of the agent calling this tool.</param>
    /// <param name="profileName">The profile the model named, or null when it named none.</param>
    /// <param name="task">The task the model stated, or null when it stated none.</param>
    /// <param name="cancellationToken">A token observing the tool call's cancellation.</param>
    /// <returns>The child's final text, or a refusal naming its reason.</returns>
    private static async Task<object> RunAsync(
        PathPolicy policy,
        IReadOnlyList<AgentProfile> profiles,
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner,
        ChildToolComposer composer,
        int depth,
        string? profileName,
        string? task,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A profile is required. The available profiles are " + Names(profiles) + ".");
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A task is required. State in full what the agent should do.");
        }

        // Ordinal, because a profile name means exactly itself and every provider compares the
        // strings a model emits the same way.
        var profile = profiles.FirstOrDefault(
            candidate => string.Equals(candidate.Name, profileName, StringComparison.Ordinal));

        if (profile is null)
        {
            // A statement of fact about this tool's own state: which profiles exist. It prescribes
            // no other tool and guesses at no near-miss.
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                "No profile is named '" + profileName + "'. The available profiles are "
                + Names(profiles) + ".");
        }

        // The budget check comes before composition so a refused run costs nothing to reach.
        var childDepth = depth + 1;
        if (childDepth > policy.Limits.MaxAgentDepth)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "Delegation is limited to "
                + policy.Limits.MaxAgentDepth.ToString(CultureInfo.InvariantCulture)
                + " levels and this agent is already at level "
                + depth.ToString(CultureInfo.InvariantCulture) + ".");
        }

        // The child's tools, composed from the child's own state. Never the parent's list filtered.
        var tools = composer(profile);

        var request = new ChildAgentRequest(
            profile.Name,
            profile.Instructions,
            tools,
            task,
            childDepth);

        var answer = await runner(request, cancellationToken).ConfigureAwait(false);

        return Report(policy, profile.Name, answer);
    }

    /// <summary>
    ///     Turns a child's final text into the parent's tool result, bounded by the result ceiling.
    /// </summary>
    /// <remarks>
    ///     A child that said nothing is reported as having said nothing rather than as a failure:
    ///     an agent can legitimately finish having found there was nothing to report, and telling
    ///     the parent it failed would invite it to retry work that was already done.
    /// </remarks>
    /// <param name="policy">The policy whose result ceiling applies.</param>
    /// <param name="profileName">The profile that ran, named in both outcomes.</param>
    /// <param name="answer">The child's final text, which may be null or empty.</param>
    /// <returns>The child's text, a statement that it said nothing, or a refusal.</returns>
    private static object Report(PathPolicy policy, string profileName, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return ToolResult.Text("The '" + profileName + "' agent finished without reporting anything.");
        }

        // A child's answer is spent out of the parent's context window like any other tool result,
        // so it is bounded by the same ceiling and refused rather than truncated: a truncated report
        // is worse than none, because the parent cannot tell that it is reading half an answer.
        if (answer.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The '" + profileName + "' agent reported "
                + answer.Length.ToString(CultureInfo.InvariantCulture)
                + " characters, which exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Text(answer);
    }

    /// <summary>
    ///     Composes the tool description, naming every profile the model may choose from.
    /// </summary>
    /// <remarks>
    ///     The profiles are part of the description rather than discoverable through a listing tool,
    ///     because a model that has to call a tool to find out what it may delegate to will usually
    ///     just not delegate. A profile's description is included where it has one.
    /// </remarks>
    /// <param name="profiles">The profiles the application registered.</param>
    /// <returns>The description presented to the model.</returns>
    private static string DescribeTool(IReadOnlyList<AgentProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            return ToolDescriptionPrefix + "none are registered, so no delegation is possible.";
        }

        var described = profiles.Select(profile => string.IsNullOrWhiteSpace(profile.Description)
            ? "'" + profile.Name + "'"
            : "'" + profile.Name + "' (" + profile.Description + ")");

        return ToolDescriptionPrefix + string.Join("; ", described) + ".";
    }

    /// <summary>
    ///     Renders the registered profile names as a quoted, comma-separated list for a refusal.
    /// </summary>
    /// <param name="profiles">The profiles the application registered.</param>
    /// <returns>The names, quoted and separated by commas, or a statement that there are none.</returns>
    private static string Names(IReadOnlyList<AgentProfile> profiles)
    {
        return profiles.Count == 0
            ? "none"
            : "'" + string.Join("', '", profiles.Select(profile => profile.Name)) + "'";
    }
}
