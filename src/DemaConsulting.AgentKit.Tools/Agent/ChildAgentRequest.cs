using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Agent;

/// <summary>
///     Everything a host needs to start one delegated agent: who it is, what it may use, and what
///     it has been asked to do.
/// </summary>
/// <remarks>
///     <para>
///     <b>The tool list on this request was built by the library, from the child's own state.</b>
///     It is not the parent's tool list, and it is not the parent's tool list with entries removed.
///     Every tool here came from a fresh <c>CreateTools</c> call made against the child's policy,
///     so any per-composition state those tools hold — a task list, a cut buffer — belongs to this
///     child alone. A host receives the list and uses it; it is never asked to supply one, because
///     the only list it could supply is its parent's.
///     </para>
///     <para>
///     <b>What a host does with it.</b> A request is handed to the runner the application gave
///     <see cref="AgentPack"/> — a
///     <c>Func&lt;ChildAgentRequest, CancellationToken, Task&lt;string&gt;&gt;</c>. The runner is
///     expected to build an agent from <see cref="Instructions"/> and <see cref="Tools"/> exactly
///     as given, run it against <see cref="Task"/>, and return its final text. It should not add
///     tools, because a tool the application did not attach is a capability the profile did not
///     admit, and it should not replace the instructions, because those are the application's
///     authorship of what the child is. Returning <see langword="null"/> or empty text is reported
///     to the parent as a child that said nothing, rather than as a failure. An exception raised
///     inside the runner propagates: failing to reach a model provider is the host's condition to
///     classify — a transient outage and a misconfigured credential look the same from inside this
///     library — so it is not converted into a refusal that would tell the model something this
///     library does not know.
///     </para>
///     <para>
///     Instances are created by the library and are immutable. There is no public constructor: a
///     request a caller could assemble would be a request a caller could assemble wrongly, and the
///     one property most worth assembling wrongly is <see cref="Tools"/>.
///     </para>
/// </remarks>
public sealed class ChildAgentRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ChildAgentRequest"/> class.
    /// </summary>
    /// <remarks>
    ///     Internal because the library is the only correct author of a request. Everything here
    ///     has been validated or constructed by <c>agent_run</c> before this point.
    /// </remarks>
    /// <param name="profileName">The name of the profile the model selected.</param>
    /// <param name="instructions">The application-authored instructions for the child.</param>
    /// <param name="tools">The child's own tools, built against the child's own policy.</param>
    /// <param name="task">The task the parent agent stated.</param>
    /// <param name="depth">The child's delegation depth, counting a root agent as zero.</param>
    internal ChildAgentRequest(
        string profileName,
        string instructions,
        IReadOnlyList<AIFunction> tools,
        string task,
        int depth)
    {
        ProfileName = profileName;
        Instructions = instructions;
        Tools = tools;
        Task = task;
        Depth = depth;
    }

    /// <summary>
    ///     Gets the name of the profile the model selected.
    /// </summary>
    /// <remarks>
    ///     Useful for naming the agent the host builds, and for the host's own logging; it carries
    ///     no authority of its own.
    /// </remarks>
    public string ProfileName { get; }

    /// <summary>
    ///     Gets the system instructions to give the child agent.
    /// </summary>
    /// <remarks>
    ///     These come from the <see cref="AgentProfile"/> the application registered. The parent
    ///     agent did not write them and could not have.
    /// </remarks>
    public string Instructions { get; }

    /// <summary>
    ///     Gets the tools the child agent may call.
    /// </summary>
    /// <remarks>
    ///     Built by the library from the child's own composition — see the type-level remarks. The
    ///     collection is already the intersection of the profile's declared names and what the
    ///     application attached, so the host attaches it as it stands. It may be empty when the
    ///     profile declares no tools, in which case the host builds an agent that can only answer.
    /// </remarks>
    public IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>
    ///     Gets the task the parent agent stated.
    /// </summary>
    /// <remarks>
    ///     This is the one part of a child's situation the parent supplies. It is data the child
    ///     works on, not authority the child carries: the child's instructions, tools and grants
    ///     all come from the application.
    /// </remarks>
    public string Task { get; }

    /// <summary>
    ///     Gets the child's delegation depth, counting the application's own root agent as zero.
    /// </summary>
    /// <remarks>
    ///     Supplied so a host can log or budget by depth. The ceiling itself is enforced by
    ///     <c>agent_run</c> against <c>ToolLimits.MaxAgentDepth</c> before a request is ever built,
    ///     so a host is never handed a request that exceeds it.
    /// </remarks>
    public int Depth { get; }
}
