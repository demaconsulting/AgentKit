namespace DemaConsulting.AgentKit.Tools.Todo;

/// <summary>
///     The statuses a task in an agent's list may carry, and the vocabulary the model states them
///     in.
/// </summary>
/// <remarks>
///     <para>
///     The statuses are held as their model-facing strings rather than as an enumeration mapped to
///     wire names, because there is exactly one vocabulary here and a second representation would
///     only create the opportunity for the two to drift. A model writes <c>in_progress</c> and the
///     store holds <c>in_progress</c>.
///     </para>
///     <para>
///     <b>Four statuses, and no more.</b> <c>pending</c>, <c>in_progress</c> and <c>done</c> are the
///     life cycle of a step. <c>blocked</c> exists because a step that cannot proceed is not the
///     same as one nobody has started, and a model with no way to say so either leaves the item
///     <c>in_progress</c> forever or quietly deletes it. There is deliberately no <c>cancelled</c>
///     or <c>deferred</c>: a step that is no longer part of the work is removed.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class TodoStatuses
{
    /// <summary>
    ///     The status of a step that has been written down but not started.
    /// </summary>
    /// <remarks>
    ///     This is the status a task takes when the model states none, because writing a step down
    ///     is the act of planning it, not of starting it.
    /// </remarks>
    public const string Pending = "pending";

    /// <summary>
    ///     The status of the step the agent is working on now.
    /// </summary>
    public const string InProgress = "in_progress";

    /// <summary>
    ///     The status of a step the agent has finished.
    /// </summary>
    public const string Done = "done";

    /// <summary>
    ///     The status of a step that cannot proceed.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Pending"/> so that a list can distinguish work nobody has
    ///     reached from work nobody can reach.
    /// </remarks>
    public const string Blocked = "blocked";

    /// <summary>
    ///     Every status a task may carry, in life-cycle order.
    /// </summary>
    /// <remarks>
    ///     Ordered for a person reading a refusal that names them, not for lookup; the collection
    ///     is four items long, so a set would cost more than it saved.
    /// </remarks>
    public static readonly IReadOnlyList<string> All = [Pending, InProgress, Done, Blocked];

    /// <summary>
    ///     Determines whether a status the model stated is one this store recognizes.
    /// </summary>
    /// <remarks>
    ///     Compared ordinally, so a status means exactly itself. A model that writes
    ///     <c>In_Progress</c> is refused rather than silently corrected: silently accepting a
    ///     variant teaches the model a vocabulary this library does not publish.
    /// </remarks>
    /// <param name="status">The status to test. May be <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when the status is one of <see cref="All"/>; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    public static bool IsValid(string? status)
    {
        return status is not null && All.Contains(status, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Renders the recognized statuses as a quoted, comma-separated list for a refusal.
    /// </summary>
    /// <remarks>
    ///     A refusal that names the permitted vocabulary is a statement of fact about the tool's
    ///     state, which is the only kind of guidance this library's refusals give.
    /// </remarks>
    /// <returns>The statuses, quoted and separated by commas.</returns>
    public static string Describe()
    {
        return "'" + string.Join("', '", All) + "'";
    }
}

/// <summary>
///     One task in an agent's list.
/// </summary>
/// <remarks>
///     A record rather than a class so that the store can hand an item out without any caller
///     being able to mutate what the store holds. A task carries no parent, no children and no
///     dependency edges; see <see cref="TodoStore"/> for why the list is flat.
/// </remarks>
/// <param name="Id">The identifier the model chose, unique within the list.</param>
/// <param name="Title">The one-line statement of what the step is.</param>
/// <param name="Status">One of <see cref="TodoStatuses.All"/>.</param>
/// <param name="Note">A free-text note, or <see langword="null"/> when the model stated none.</param>
internal sealed record TodoItem(string Id, string Title, string Status, string? Note);

/// <summary>
///     The flat task list belonging to one agent, shared by the tools of one <see cref="TodoPack"/>
///     composition.
/// </summary>
/// <remarks>
///     <para>
///     <b>The store belongs to the agent instance whose tools were built from it, and is not shared
///     with a delegated agent.</b> One instance is allocated per <see cref="TodoPack.CreateTools"/>
///     call and shared between that composition's list, set and remove tools, exactly as the text
///     family allocates one cut/paste buffer per composition. A delegated agent's tools are built
///     by a separate <c>CreateTools</c> call and therefore get a separate store, which is what keeps
///     a sub-agent's checklist out of its parent's list. This is not a convention a caller can opt
///     out of: the store is internal, no public type accepts one, and the tool factories can only
///     be reached from <see cref="TodoPack.CreateTools"/>, which allocates a fresh store every time.
///     </para>
///     <para>
///     <b>The list is flat, and that is a finding rather than a simplification.</b> A nested list
///     was built and measured: a parent's single "delegate an inspection" step became the
///     sub-agent's own four-phase list, so the hierarchy the tree was meant to express was already
///     being expressed by delegation. A <c>blockedBy</c> edge was built too, offered prominently in
///     the tool description alongside a strong instruction to track work, and was used zero times
///     in three runs while costing fourteen todo calls against ten without it. Sequence is carried
///     by list order and by status; nothing else is needed and everything else was paid for.
///     </para>
///     <para>
///     <b>Insertion order is the list's order, and an update keeps an item where it was.</b> The
///     order a model wrote its steps down in is the order it means to do them in, so a status
///     change must not reorder the plan under it.
///     </para>
///     <para>
///     <b>The store is in memory and lives exactly as long as the tools built from it.</b> A task
///     list is the working state of one agent run, not a document; when the composition is
///     discarded, so is the list.
///     </para>
///     <para>
///     Access is guarded by a lock, so concurrent tool calls against one composition are safe.
///     </para>
/// </remarks>
internal sealed class TodoStore
{
    /// <summary>
    ///     The lock guarding <see cref="_items"/> so concurrent tool calls are safe.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The tasks in the order the model wrote them down.
    /// </summary>
    /// <remarks>
    ///     A list rather than a dictionary because order is meaningful and the lists an agent
    ///     keeps are short; a linear scan over a handful of items costs less than maintaining a
    ///     second structure to preserve the order a dictionary would lose.
    /// </remarks>
    private readonly List<TodoItem> _items = [];

    /// <summary>
    ///     Adds a task or updates the one already carrying the identifier.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Upsert rather than separate add and update operations: a model that has to know whether
    ///     an item already exists before it can record a status will sometimes guess wrong, and the
    ///     resulting refusal costs a turn and teaches it nothing. Writing the step down and
    ///     advancing it are the same call.
    ///     </para>
    ///     <para>
    ///     An update replaces the title, status and note, and leaves the item at its position.
    ///     Identifiers are compared ordinally, so an identifier means exactly itself.
    ///     </para>
    /// </remarks>
    /// <param name="id">The identifier to add or update. Must be non-null and non-empty.</param>
    /// <param name="title">The step's one-line statement. Must be non-null and non-empty.</param>
    /// <param name="status">
    ///     The status to record. Must be one of <see cref="TodoStatuses.All"/>; the caller is
    ///     expected to have refused an unrecognized status before reaching here.
    /// </param>
    /// <param name="note">The note to record, or <see langword="null"/> for none.</param>
    /// <returns>The number of tasks the list holds after the change.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="id"/> or <paramref name="title"/> is <see langword="null"/>
    ///     or empty, or when <paramref name="status"/> is not a recognized status.
    /// </exception>
    public int Set(string id, string title, string status, string? note)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(title);

        // An unrecognized status is refused by the tool before it reaches the store, so arriving
        // here with one is a defect in this library rather than something a model did.
        if (!TodoStatuses.IsValid(status))
        {
            throw new ArgumentException(
                "The status must be one of " + TodoStatuses.Describe() + ".",
                nameof(status));
        }

        lock (_gate)
        {
            var index = IndexOf(id);
            var item = new TodoItem(id, title, status, note);

            // Replace in place so a status change never reorders the plan beneath it.
            if (index < 0)
            {
                _items.Add(item);
            }
            else
            {
                _items[index] = item;
            }

            return _items.Count;
        }
    }

    /// <summary>
    ///     Removes the task carrying an identifier.
    /// </summary>
    /// <remarks>
    ///     Removing a task that is not there is reported as a miss rather than treated as success,
    ///     so the tool can state the fact instead of letting a model believe it tidied something up.
    /// </remarks>
    /// <param name="id">The identifier to remove. Must be non-null and non-empty.</param>
    /// <param name="remaining">On return, the number of tasks the list holds.</param>
    /// <returns>
    ///     <see langword="true"/> when a task was removed; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="id"/> is <see langword="null"/> or empty.
    /// </exception>
    public bool Remove(string id, out int remaining)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        lock (_gate)
        {
            var index = IndexOf(id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }

            remaining = _items.Count;
            return index >= 0;
        }
    }

    /// <summary>
    ///     Reports the tasks in the order the model wrote them down.
    /// </summary>
    /// <remarks>
    ///     A copy is returned so the caller reads a stable snapshot rather than a collection
    ///     another tool call could be mutating underneath it. The items themselves are records and
    ///     cannot be altered.
    /// </remarks>
    /// <returns>The tasks, in list order.</returns>
    public IReadOnlyList<TodoItem> Items()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    /// <summary>
    ///     Lists the identifiers the list currently holds, in list order.
    /// </summary>
    /// <remarks>
    ///     This exists so a removal refusal can tell a model which identifiers do exist when the
    ///     one it named does not — the usual cause is a near-miss on an identifier the model chose
    ///     itself. It is deliberately internal rather than a tool of its own, because
    ///     <c>todo_list</c> already reports the list.
    /// </remarks>
    /// <returns>The identifiers, in list order.</returns>
    public IReadOnlyList<string> Identifiers()
    {
        lock (_gate)
        {
            return [.. _items.Select(item => item.Id)];
        }
    }

    /// <summary>
    ///     Finds the position of a task by identifier.
    /// </summary>
    /// <remarks>
    ///     Callers hold <see cref="_gate"/>; this member does not take it, because both of its
    ///     callers need the lookup and the subsequent mutation to be one atomic step.
    /// </remarks>
    /// <param name="id">The identifier to find.</param>
    /// <returns>The zero-based position, or -1 when no task carries the identifier.</returns>
    private int IndexOf(string id)
    {
        return _items.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }
}
