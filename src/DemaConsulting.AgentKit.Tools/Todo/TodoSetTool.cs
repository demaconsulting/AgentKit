using System.ComponentModel;
using System.Globalization;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Todo;

/// <summary>
///     The <c>todo_set</c> tool: writes a step into the agent's task list, or advances the step
///     already carrying that identifier.
/// </summary>
/// <remarks>
///     <para>
///     <b>One call both records a step and advances it.</b> The tool upserts by <c>id</c>: an
///     identifier the list does not hold adds a task at the end, and one it does hold replaces that
///     task's title, status and note where it stands. There is no separate add and update, because
///     a model that must know whether an item already exists before it may record a status will
///     sometimes guess wrong, and the refusal costs a turn and teaches it nothing.
///     </para>
///     <para>
///     <b>The status defaults to <c>pending</c>.</b> Writing a step down is planning it, not
///     starting it, so the common case — listing the phases of a job up front — needs no status
///     argument at all.
///     </para>
///     <para>
///     <b>Reliable task tracking requires the application to instruct the agent to use this
///     family.</b> Attaching the tools is not enough; see <see cref="TodoPack"/>, which carries the
///     measurements and the instruction text to copy. An application that attaches this tool
///     without such an instruction will observe that the model mostly does not call it.
///     </para>
///     <para>
///     The result states the task's identifier, the status it now carries and the size of the list,
///     so the model does not have to call <c>todo_list</c> to find out what its own write did.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store it was given, whose own access is locked.
///     </para>
/// </remarks>
public static class TodoSetTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "todo_set";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Records a step in your task list, or updates the step already carrying that id. Supply a "
        + "short stable id and a one-line title; optionally a status of 'pending', 'in_progress', "
        + "'done' or 'blocked' (default 'pending') and a free-text note. A new id appends a task, "
        + "an existing id replaces that task where it stands. Returns the task's new status and "
        + "the size of the list, or a denial explaining why the request was refused.";

    /// <summary>
    ///     Creates the <c>todo_set</c> tool over one agent's task list.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment, and because the
    ///     store parameter is how a task list is bound to exactly one agent. An application obtains
    ///     this tool by attaching <see cref="TodoPack"/>, which allocates a fresh store for every
    ///     composition; there is deliberately no public path that would let a caller hand one
    ///     agent's store to another agent's tools.
    /// </remarks>
    /// <param name="store">The task list this tool writes to.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(TodoStore store)
    {
        // A missing store is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);

        // Declared to return object on purpose; see the remarks on GuardedToolFactory.
        var set = (
                [Description(
                    "A short, stable identifier for the step, for example 'phase2'. An id the list "
                    + "already holds updates that step in place.")]
                string? id = null,
                [Description("A one-line statement of what the step is.")]
                string? title = null,
                [Description(
                    "The status of the step: 'pending', 'in_progress', 'done' or 'blocked'. "
                    + "Optional; omit it to record 'pending'.")]
                string? status = null,
                [Description("A free-text note about the step. Optional.")]
                string? note = null) =>
            Set(store, id, title, status, note);

        return GuardedToolFactory.Create(set, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Writes or advances one task, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <param name="store">The task list to write to.</param>
    /// <param name="id">The identifier the model supplied, or null when it supplied none.</param>
    /// <param name="title">The title the model supplied, or null when it supplied none.</param>
    /// <param name="status">The status the model supplied, or null to record the default.</param>
    /// <param name="note">The note the model supplied, or null for none.</param>
    /// <returns>A statement of what the list now holds, or a refusal naming its reason.</returns>
    private static object Set(
        TodoStore store,
        string? id,
        string? title,
        string? status,
        string? note)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(id))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "An id is required. Supply a short, stable identifier for the step, for example "
                + "'phase2'.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A title is required. Supply a one-line statement of what the step is.");
        }

        // An omitted status records the default rather than being an error: writing a step down is
        // planning it, and planning it is what 'pending' means.
        var recorded = string.IsNullOrWhiteSpace(status) ? TodoStatuses.Pending : status;
        if (!TodoStatuses.IsValid(recorded))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The status must be one of " + TodoStatuses.Describe() + ".");
        }

        // An empty note is the same as no note; holding one would report a note that says nothing.
        var recordedNote = string.IsNullOrWhiteSpace(note) ? null : note;

        var count = store.Set(id, title, recorded, recordedNote);

        // The result states the outcome and the size of the list so the model does not have to
        // call todo_list to learn what its own write did.
        return ToolResult.Text(
            "Task '" + id + "' is " + recorded + ". The list now has " + Count(count) + ".");
    }

    /// <summary>
    ///     Renders a task count as the phrase a result sentence ends with.
    /// </summary>
    /// <remarks>
    ///     Singular and plural are distinguished because a result that reads "1 items" is the kind
    ///     of small wrongness that makes a model doubt the rest of the sentence.
    /// </remarks>
    /// <param name="count">The number of tasks the list holds.</param>
    /// <returns>The count and its noun, for example <c>5 items</c>.</returns>
    internal static string Count(int count)
    {
        return count.ToString(CultureInfo.InvariantCulture) + (count == 1 ? " item" : " items");
    }
}
