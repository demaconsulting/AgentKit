namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The text-only cut/paste buffer store shared by <see cref="TextFileCutLinesTool"/> and
///     <see cref="TextFilePasteLinesTool"/>.
/// </summary>
/// <remarks>
///     <para>
///     <b>Every deletion of a range of lines is recoverable.</b> The text family removes a range of
///     lines only through <see cref="TextFileCutLinesTool"/>, and that tool always captures what it
///     removed into a named slot here before deleting it, so a mistaken cut is undone by pasting the
///     slot back. The store is what makes "cut" a move rather than a destruction.
///     </para>
///     <para>
///     <b>Slots are named, and a later capture into a name replaces what was there.</b> A model can
///     hold several cut fragments at once by giving each a distinct name, and re-using a name is a
///     deliberate overwrite — the newest capture wins, exactly as a clipboard does. There is
///     deliberately no peek or clear operation: the buffer is a staging area for a move, not a
///     durable document store, and a fragment left behind is harmless because it holds only text.
///     </para>
///     <para>
///     <b>The store holds text and nothing else, and lives for the lifetime of the tool
///     instances.</b> One instance is allocated per <see cref="TextFilePack.CreateTools"/> call and
///     shared between that composition's cut and paste tools, so two independently composed tool sets
///     never share slots. When the composition is discarded, so is the store.
///     </para>
///     <para>
///     Access is guarded by a lock, so concurrent tool calls against one composition are safe. The
///     family advertises thread safety, and this store is where that promise is kept for cut and
///     paste.
///     </para>
/// </remarks>
internal sealed class TextFileLineBuffers
{
    /// <summary>
    ///     The name used when a cut or paste request supplies no slot name.
    /// </summary>
    /// <remarks>
    ///     A single default slot keeps the common case — cut a range, paste it straight back —
    ///     free of any name the model has to invent or remember.
    /// </remarks>
    public const string DefaultSlot = "default";

    /// <summary>
    ///     The lock guarding <see cref="_slots"/> so concurrent tool calls are safe.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The captured text keyed by slot name, compared ordinally so a name means exactly itself.
    /// </summary>
    private readonly Dictionary<string, string> _slots = new(StringComparer.Ordinal);

    /// <summary>
    ///     Captures text into a slot, replacing whatever the slot previously held.
    /// </summary>
    /// <remarks>
    ///     A capture into an occupied slot overwrites it: the newest cut wins, which is the least
    ///     surprising clipboard behavior and avoids a second name the model would have to manage.
    /// </remarks>
    /// <param name="name">The slot name to capture into. Must be non-null and non-empty.</param>
    /// <param name="text">The text to capture. Must be non-null; an empty capture is permitted.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public void Capture(string name, string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(text);

        lock (_gate)
        {
            _slots[name] = text;
        }
    }

    /// <summary>
    ///     Retrieves the text a slot holds, without removing it.
    /// </summary>
    /// <remarks>
    ///     Paste does not consume the slot, so the same captured fragment can be pasted into more
    ///     than one place. An empty slot is reported as a miss rather than as empty text, so the
    ///     paste tool can tell a model there is nothing to paste.
    /// </remarks>
    /// <param name="name">The slot name to read. Must be non-null and non-empty.</param>
    /// <param name="text">
    ///     On success, the captured text; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the slot holds captured text; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="name"/> is <see langword="null"/> or empty.
    /// </exception>
    public bool TryPaste(string name, out string? text)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        lock (_gate)
        {
            return _slots.TryGetValue(name, out text);
        }
    }

    /// <summary>
    ///     Lists the names of the slots that currently hold captured text, in ordinal order.
    /// </summary>
    /// <remarks>
    ///     This exists so a paste refusal can tell a model which slots do hold content when the
    ///     slot it asked for is empty — the common recovery is that the model captured into a
    ///     named slot and then omitted the same name when pasting. It is deliberately internal
    ///     guidance rather than a tool: the family publishes no buffer inspection tool, because
    ///     cut and copy already report what they captured.
    /// </remarks>
    /// <returns>The populated slot names, ordered ordinally.</returns>
    public IReadOnlyList<string> PopulatedSlots()
    {
        lock (_gate)
        {
            var names = new List<string>(_slots.Keys);
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }
}
