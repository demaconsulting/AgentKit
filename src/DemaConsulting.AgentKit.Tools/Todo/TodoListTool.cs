using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Todo;

/// <summary>
///     The <c>todo_list</c> tool: reports the agent's task list, in the order the steps were
///     written down.
/// </summary>
/// <remarks>
///     <para>
///     <b>The list is flat and its order is its sequence.</b> There is no tree and there are no
///     dependency edges, so a reader of this result learns what to do next from the order of the
///     items and from their statuses, and from nothing else. See <see cref="TodoStore"/> for the
///     measurements behind that shape.
///     </para>
///     <para>
///     <b>The tool takes no arguments.</b> A filter by status was considered and rejected: the
///     lists an agent keeps are short, so filtering would save no context worth the extra argument
///     the model has to reason about, and a filtered view is exactly how an item gets forgotten.
///     </para>
///     <para>
///     <b>Reliable task tracking requires the application to instruct the agent to use this
///     family.</b> Attaching the tools is not enough; see <see cref="TodoPack"/>, which carries the
///     measurements and the instruction text to copy.
///     </para>
///     <para>
///     An empty list is reported as an empty list, not refused. Having written nothing down is a
///     true and useful answer, and a refusal would invite the model to conclude the tool is broken.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store it was given, whose own access is locked.
///     </para>
/// </remarks>
public static class TodoListTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "todo_list";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reports your task list: every step you have recorded with todo_set, in the order you "
        + "recorded it, each with its id, title, status and note. Takes no arguments. The order is "
        + "the sequence; there is no nesting and there are no dependencies.";

    /// <summary>
    ///     Creates the <c>todo_list</c> tool over one agent's task list.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment, and because the
    ///     store parameter is how a task list is bound to exactly one agent. An application obtains
    ///     this tool by attaching <see cref="TodoPack"/>.
    /// </remarks>
    /// <param name="store">The task list this tool reports.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(TodoStore store)
    {
        // A missing store is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);

        // Declared to return object on purpose; see the remarks on GuardedToolFactory.
        var list = () => List(store);

        return GuardedToolFactory.Create(list, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Reports the list as structured data.
    /// </summary>
    /// <remarks>
    ///     Structured rather than rendered text, because the model has to read individual fields
    ///     back — an identifier it will pass to <c>todo_set</c>, a status it will advance — and a
    ///     rendered list makes it re-parse its own plan.
    /// </remarks>
    /// <param name="store">The task list to report.</param>
    /// <returns>The structured list: the item count and the items in order.</returns>
    private static object List(TodoStore store)
    {
        var items = store.Items();

        return ToolResult.Structured(new
        {
            itemCount = items.Count,
            items = items
                .Select(item => new
                {
                    id = item.Id,
                    title = item.Title,
                    status = item.Status,
                    note = item.Note,
                })
                .ToList(),
        });
    }
}
