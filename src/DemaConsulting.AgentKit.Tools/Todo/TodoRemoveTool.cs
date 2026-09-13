using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Todo;

/// <summary>
///     The <c>todo_remove</c> tool: drops a step from the agent's task list.
/// </summary>
/// <remarks>
///     <para>
///     <b>Removal is for a step that is no longer part of the work, not for a step that is
///     finished.</b> A finished step is marked <c>done</c> with <c>todo_set</c> and stays in the
///     list, because the list is how a person following along sees what was accomplished. This tool
///     exists for the case where a plan turns out to contain a step that should never have been in
///     it.
///     </para>
///     <para>
///     <b>An unknown identifier is refused with a plain statement of fact.</b> The refusal says
///     that no task carries the identifier and, when the list holds others, names them — that is a
///     statement about the tool's own state, the same kind the text family's paste refusal makes
///     when a buffer slot is empty. It prescribes nothing: it does not tell the model to call
///     <c>todo_list</c>, and it does not guess which identifier was meant.
///     </para>
///     <para>
///     <b>Reliable task tracking requires the application to instruct the agent to use this
///     family.</b> Attaching the tools is not enough; see <see cref="TodoPack"/>, which carries the
///     measurements and the instruction text to copy.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store it was given, whose own access is locked.
///     </para>
/// </remarks>
public static class TodoRemoveTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "todo_remove";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Drops a step from your task list by id, for a step that is no longer part of the work. A "
        + "step you have finished is marked 'done' with todo_set and kept, so the list shows what "
        + "was accomplished. Returns the size of the list, or a denial explaining why the request "
        + "was refused.";

    /// <summary>
    ///     Creates the <c>todo_remove</c> tool over one agent's task list.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment, and because the
    ///     store parameter is how a task list is bound to exactly one agent. An application obtains
    ///     this tool by attaching <see cref="TodoPack"/>.
    /// </remarks>
    /// <param name="store">The task list this tool removes from.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(TodoStore store)
    {
        // A missing store is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);

        // Declared to return object on purpose; see the remarks on GuardedToolFactory.
        var remove = (
                [Description("The id of the step to drop, for example 'phase2'.")]
                string? id = null) =>
            Remove(store, id);

        return GuardedToolFactory.Create(remove, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Removes one task, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <param name="store">The task list to remove from.</param>
    /// <param name="id">The identifier the model supplied, or null when it supplied none.</param>
    /// <returns>A statement of what the list now holds, or a refusal naming its reason.</returns>
    private static object Remove(TodoStore store, string? id)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(id))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "An id is required. Supply the id of the step to drop, for example 'phase2'.");
        }

        if (store.Remove(id, out var remaining))
        {
            return ToolResult.Text(
                "Task '" + id + "' was removed. The list now has "
                + TodoSetTool.Count(remaining) + ".");
        }

        // A miss is a statement of fact about the list, naming what it does hold. It prescribes no
        // tool and guesses at no identifier.
        var identifiers = store.Identifiers();
        var message = identifiers.Count == 0
            ? "No task carries the id '" + id + "'. The list is empty."
            : "No task carries the id '" + id + "'. The list holds '"
              + string.Join("', '", identifiers) + "'.";

        return ToolResult.Denied(DenialReason.TargetNotFound, message);
    }
}
