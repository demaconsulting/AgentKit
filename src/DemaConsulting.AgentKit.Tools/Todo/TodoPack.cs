using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Todo;

/// <summary>
///     The todo tool family: the pack an application attaches to give an agent a task list it can
///     write down, advance and close out.
/// </summary>
/// <remarks>
///     <para>
///     <b>Attaching these tools is not enough. The application must instruct the agent to use
///     them.</b> This is the single most important thing to know about this family, and it is
///     stated here rather than buried in a sample because an author who attaches the tools without
///     such an instruction will watch the model ignore them and conclude they do not work. A model
///     left to its own judgement mostly relies on its own memory: it can hold a five-phase plan in
///     its head well enough to feel no need for a list, right up to the point where it forgets the
///     fourth phase, and neither it nor the user can see that happening.
///     </para>
///     <para>
///     The difference is measured, not asserted. Against a five-phase task:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///             With a soft instruction — "keep track of multi-step work using your task list so
///             progress is visible" — the family was used in <b>1 of 5 runs</b>, and the single run
///             that used it left an item stranded <c>in_progress</c>.
///             </description>
///         </item>
///         <item>
///             <description>
///             With an explicit instruction, it was used in <b>3 of 3 runs</b>, with all five
///             phases recorded and closed, and near-identical behavior each time.
///             </description>
///         </item>
///     </list>
///     <para>
///     <b>Copy this into the agent's instructions</b> — it is the wording that produced the 3 of 3
///     result, and it is published as <see cref="SuggestedInstruction"/> so an application can
///     append it rather than transcribe it:
///     </para>
///     <para>
///     <i>Before beginning any multi-step work, write the steps down with <c>todo_set</c>. Mark each
///     item <c>in_progress</c> when you start it and <c>done</c> the moment you finish it, and keep
///     the list current as you go. The list is how you guarantee nothing is forgotten or skipped,
///     and how the user follows your progress — do not rely on memory for multi-step work.</i>
///     </para>
///     <para>
///     <b>The list is flat, one per agent, and belongs to that agent alone.</b> There is no tree, no
///     nesting, no dependency edge and no replace-the-whole-list tool; each was built, measured and
///     rejected, and <see cref="TodoStore"/> records why. A fresh store is allocated per
///     <see cref="CreateTools"/> call, so a delegated agent composed through <c>AgentPack</c> keeps
///     its own list and cannot write into its parent's.
///     </para>
///     <para>
///     <b>The family asks nothing of the host and touches no files.</b> Its tools take no path and
///     never consult the policy they are handed, so the policy is accepted and ignored; the list
///     lives in memory for exactly as long as the composition that created it.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family and — the part that actually makes it work — instructing the agent to
///     use it. The instruction is appended to whatever the application has to say about the agent's
///     own job; without it the model will mostly track its work in its own head.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(workspace, [PathRule.ReadWrite(workspace)]);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .Add(new TextFilePack())
///         .Add(new TodoPack())
///         .Build();
///
///     // todo_list, todo_set, todo_remove — one list, belonging to the agent built from these tools.
///     var instructions =
///         "You are a release assistant working in the permitted locations.\n\n"
///         + TodoPack.SuggestedInstruction;
///     </code>
/// </example>
public sealed class TodoPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "todo";

    /// <summary>
    ///     The instruction an application should give an agent that carries this family.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Published as a constant so an application can append it to its own instructions rather
    ///     than transcribe it, and so the wording that was measured cannot drift from the wording
    ///     that ships. An application is free to write its own; this is the text that produced
    ///     three of three runs with every phase recorded and closed, where a softer instruction
    ///     produced one of five. The type-level remarks carry both measurements.
    ///     </para>
    ///     <para>
    ///     It is deliberately not applied automatically. This library composes tools; it does not
    ///     author an agent's instructions, and silently injecting text into a system prompt is
    ///     exactly the kind of invisible behavior an application author cannot audit.
    ///     </para>
    /// </remarks>
    public const string SuggestedInstruction =
        "Before beginning any multi-step work, write the steps down with todo_set. Mark each item "
        + "in_progress when you start it and done the moment you finish it, and keep the list "
        + "current as you go. The list is how you guarantee nothing is forgotten or skipped, and "
        + "how the user follows your progress — do not rely on memory for multi-step work.";

    /// <summary>
    ///     Initializes a new instance of the <see cref="TodoPack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and holds no task list: a list is allocated per
    ///     <see cref="CreateTools"/> call rather than on the pack, which is what gives each
    ///     composed agent its own. Declared explicitly rather than left implicit so that the
    ///     documentation the package ships describes every public member.
    /// </remarks>
    public TodoPack()
    {
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
    ///     <see cref="HostCapabilities.None"/>: an in-memory task list asks nothing of the host, so
    ///     the family is registered by every composition rather than gated behind a declaration an
    ///     application would have to know to make.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools over a task list belonging to this composition alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>A fresh <see cref="TodoStore"/> is allocated here, on every call.</b> That is what
    ///     binds a task list to one agent: two compositions never share a list, and a delegated
    ///     agent — whose tools are created by a separate call to this method — therefore keeps its
    ///     own. The store is a local, never a field and never a parameter, so there is no way to
    ///     express handing one agent's list to another agent's tools.
    ///     </para>
    ///     <para>
    ///     The order — list, set, remove — is fixed rather than incidental, because the order a
    ///     model sees the tools in is observable. The policy is accepted and ignored: this family
    ///     touches no files, so it has nothing to judge against a policy. It is still validated, so
    ///     that a composing application that forgot one is told at the point it forgot.
    ///     </para>
    /// </remarks>
    /// <param name="policy">
    ///     The access policy of the composition. Required for the contract but unused: no tool in
    ///     this family reads, writes or names a path.
    /// </param>
    /// <returns>The list, set and remove tools, in that order.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated even though it is unused, so that a composing application that omitted a policy
        // is told at the point it made the mistake rather than by a sibling family later.
        ArgumentNullException.ThrowIfNull(policy);

        // One list per composition. A local, not a field and not a parameter: this is the whole
        // mechanism by which a delegated agent cannot reach its parent's list.
        var store = new TodoStore();

        return
        [
            TodoListTool.Create(store),
            TodoSetTool.Create(store),
            TodoRemoveTool.Create(store)
        ];
    }
}
