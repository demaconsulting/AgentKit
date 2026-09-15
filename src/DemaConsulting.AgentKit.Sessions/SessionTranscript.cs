using System.Collections.ObjectModel;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     What one transcript entry is, which decides how it is framed for a provider and whether it
///     may be separated from its neighbor.
/// </summary>
public enum TranscriptEntryKind
{
    /// <summary>
    ///     A message the application or its user sent to the model.
    /// </summary>
    UserMessage,

    /// <summary>
    ///     A message the model produced.
    /// </summary>
    AssistantMessage,

    /// <summary>
    ///     The model's request to invoke a tool, identified so its result can be paired with it.
    /// </summary>
    ToolCall,

    /// <summary>
    ///     The outcome of a tool invocation, identified so it can be paired with its call.
    /// </summary>
    ToolResult,

    /// <summary>
    ///     A consolidated record of older history, produced by the summarizer and seeded into a
    ///     fresh provider session rather than produced within one.
    /// </summary>
    ContextRecord,
}

/// <summary>
///     One indivisible item of session history, as the engine records it out of session.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why the engine keeps its own transcript.</b> Consolidation must run <em>out of
///     session</em>: asking a live session to summarize itself spends that session's own context
///     on the summary and triggers the provider's built-in compactor, which is self-defeating.
///     The engine therefore maintains this record itself and hands the material to a separate,
///     stateless summarizer call. That also means the transcript is available when a provider
///     session has been disposed, which is exactly when a fresh one must be seeded.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class TranscriptEntry
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TranscriptEntry"/> class.
    /// </summary>
    /// <remarks>
    ///     The identifier is required for a tool call and a tool result, and rejected for
    ///     everything else. That asymmetry is deliberate: pairing a call with its result is what
    ///     lets a tier boundary be snapped so the two never separate, and an unidentified call
    ///     could not be paired. An identifier on a plain message would be meaningless and is
    ///     refused rather than ignored, so a caller that supplies one learns it misunderstood the
    ///     model.
    /// </remarks>
    /// <param name="kind">The kind of entry this is.</param>
    /// <param name="text">
    ///     The entry's own text. Must not be <see langword="null"/>; may be empty, because a tool
    ///     that returns nothing still occupies a turn.
    /// </param>
    /// <param name="toolCallId">
    ///     The identifier pairing a call with its result. Required and non-blank when
    ///     <paramref name="kind"/> is <see cref="TranscriptEntryKind.ToolCall"/> or
    ///     <see cref="TranscriptEntryKind.ToolResult"/>; must be <see langword="null"/> otherwise.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="kind"/> is not a defined <see cref="TranscriptEntryKind"/> member.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="kind"/> is a tool call or tool result and <paramref name="toolCallId"/>
    ///     is <see langword="null"/> or blank, or <paramref name="kind"/> is any other kind and
    ///     <paramref name="toolCallId"/> is not <see langword="null"/>.
    /// </exception>
    public TranscriptEntry(TranscriptEntryKind kind, string text, string? toolCallId = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        // An undefined kind is a defect in the caller, not history. It would pass the pairing
        // rules below - it is neither a call nor a result, so no identifier is required and none
        // is refused - be accepted, and then render through ToTranscriptLine's default branch as
        // though it were a consolidated record, making malformed input part of the context an
        // agent is seeded from. Refused here, following ToolResult.Denied's precedent.
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The entry kind must be a defined TranscriptEntryKind member.");
        }

        // Validate the pairing identifier before any assignment, so an entry that could not be
        // paired - and would therefore be capable of orphaning a tool call at a tier boundary -
        // never exists even briefly.
        var pairing = kind is TranscriptEntryKind.ToolCall or TranscriptEntryKind.ToolResult;
        if (pairing && string.IsNullOrWhiteSpace(toolCallId))
        {
            throw new ArgumentException(
                "A tool call or tool result requires a non-blank tool call identifier.",
                nameof(toolCallId));
        }

        if (!pairing && toolCallId is not null)
        {
            throw new ArgumentException(
                $"A {kind} entry carries no tool call identifier.",
                nameof(toolCallId));
        }

        Kind = kind;
        Text = text;
        ToolCallId = toolCallId;
        EstimatedTokens = TokenEstimator.EstimateTokens(text) + TokenEstimator.PerEntryOverheadTokens;
    }

    /// <summary>
    ///     Gets the kind of entry this is.
    /// </summary>
    public TranscriptEntryKind Kind { get; }

    /// <summary>
    ///     Gets the entry's own text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the identifier pairing a tool call with its result, or <see langword="null"/> for
    ///     any other kind of entry.
    /// </summary>
    public string? ToolCallId { get; }

    /// <summary>
    ///     Gets the estimated tokens this entry occupies in a context window, including framing.
    /// </summary>
    /// <remarks>
    ///     Computed once at construction because the entry is immutable, and because every tier
    ///     boundary decision walks the transcript and would otherwise re-estimate the same text on
    ///     every rotation.
    /// </remarks>
    public int EstimatedTokens { get; }

    /// <summary>
    ///     Creates a user message entry.
    /// </summary>
    /// <param name="text">The message text. Must not be <see langword="null"/>.</param>
    /// <returns>A user message entry carrying <paramref name="text"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TranscriptEntry User(string text) => new(TranscriptEntryKind.UserMessage, text);

    /// <summary>
    ///     Creates an assistant message entry.
    /// </summary>
    /// <param name="text">The message text. Must not be <see langword="null"/>.</param>
    /// <returns>An assistant message entry carrying <paramref name="text"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TranscriptEntry Assistant(string text) => new(TranscriptEntryKind.AssistantMessage, text);

    /// <summary>
    ///     Creates a tool call entry.
    /// </summary>
    /// <param name="toolCallId">
    ///     The identifier the matching result will carry. Must not be <see langword="null"/> or blank.
    /// </param>
    /// <param name="text">The call as recorded. Must not be <see langword="null"/>.</param>
    /// <returns>A tool call entry that can be paired with its result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="toolCallId"/> is <see langword="null"/> or blank.</exception>
    public static TranscriptEntry ToolCall(string toolCallId, string text) =>
        new(TranscriptEntryKind.ToolCall, text, toolCallId);

    /// <summary>
    ///     Creates a tool result entry.
    /// </summary>
    /// <param name="toolCallId">
    ///     The identifier of the call this answers. Must not be <see langword="null"/> or blank.
    /// </param>
    /// <param name="text">The result as recorded. Must not be <see langword="null"/>.</param>
    /// <returns>A tool result entry that can be paired with its call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="toolCallId"/> is <see langword="null"/> or blank.</exception>
    public static TranscriptEntry ToolResult(string toolCallId, string text) =>
        new(TranscriptEntryKind.ToolResult, text, toolCallId);

    /// <summary>
    ///     Creates a consolidated record entry, as seeded into a fresh provider session.
    /// </summary>
    /// <param name="text">The consolidated record. Must not be <see langword="null"/>.</param>
    /// <returns>A context record entry carrying <paramref name="text"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static TranscriptEntry ContextRecord(string text) =>
        new(TranscriptEntryKind.ContextRecord, text);

    /// <summary>
    ///     Renders this entry as one labeled line of consolidation material.
    /// </summary>
    /// <remarks>
    ///     The summarizer is a stateless call that receives text, so the material handed to it must
    ///     say who said what. The label is fixed and mechanical rather than prose, so that the same
    ///     history always renders to the same string and a test can assert on it exactly.
    /// </remarks>
    /// <returns>A single labeled rendering of this entry.</returns>
    public string ToTranscriptLine() => Kind switch
    {
        TranscriptEntryKind.UserMessage => $"USER: {Text}",
        TranscriptEntryKind.AssistantMessage => $"ASSISTANT: {Text}",
        TranscriptEntryKind.ToolCall => $"TOOL CALL [{ToolCallId}]: {Text}",
        TranscriptEntryKind.ToolResult => $"TOOL RESULT [{ToolCallId}]: {Text}",
        _ => $"RECORD: {Text}",
    };
}

/// <summary>
///     The append-only verbatim history the engine keeps out of session, and the source of every
///     tier boundary decision.
/// </summary>
/// <remarks>
///     <para>
///     <b>Append-only between rotations, by design.</b> Nothing already sent to a provider is ever
///     rewritten while a session is live. That is what preserves prompt caching: a provider that
///     recognizes an unchanged prefix charges less for it, and an in-place edit anywhere in the
///     history invalidates that prefix for every following turn. All reshaping happens in one
///     batch at rotation, where the cache is invalidated anyway because the session is being
///     replaced.
///     </para>
///     <para>
///     Instances are immutable: <see cref="Append(TranscriptEntry)"/> returns a new transcript
///     rather than mutating this one. That makes the rotation engine a pure function of its inputs
///     and lets a test hold a before-and-after pair. Instances are safe for concurrent use.
///     </para>
/// </remarks>
public sealed class SessionTranscript
{
    /// <summary>
    ///     The entries, oldest first.
    /// </summary>
    private readonly TranscriptEntry[] _entries;

    /// <summary>
    ///     The read-only view handed out by <see cref="Entries"/>.
    /// </summary>
    /// <remarks>
    ///     Built once at construction rather than per read. Handing out the backing array would let
    ///     a caller cast it back to <c>TranscriptEntry[]</c> and replace an entry — including with
    ///     <see langword="null"/> — corrupting the token total cached below and every rotation
    ///     decision taken from it.
    /// </remarks>
    private readonly ReadOnlyCollection<TranscriptEntry> _entriesView;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SessionTranscript"/> class.
    /// </summary>
    /// <remarks>
    ///     Private because a transcript is only ever built from <see cref="Empty"/> by appending.
    ///     Taking ownership of the array avoids copying it on every append, which is safe because
    ///     every caller is within this class and passes a freshly allocated array.
    /// </remarks>
    /// <param name="entries">The entries, oldest first. Ownership passes to this instance.</param>
    private SessionTranscript(TranscriptEntry[] entries)
    {
        _entries = entries;
        _entriesView = Array.AsReadOnly(entries);

        // Sum once at construction: the total is consulted on every turn to decide whether the
        // rotation threshold has been reached, and the entry list never changes afterwards.
        var total = 0;
        foreach (var entry in entries)
        {
            total += entry.EstimatedTokens;
        }

        EstimatedTokens = total;
    }

    /// <summary>
    ///     Gets the transcript holding no entries.
    /// </summary>
    /// <remarks>
    ///     A single shared instance rather than a factory method, because the type is immutable and
    ///     sharing it is therefore free of risk.
    /// </remarks>
    public static SessionTranscript Empty { get; } = new([]);

    /// <summary>
    ///     Gets the entries, oldest first.
    /// </summary>
    /// <remarks>
    ///     A genuine read-only view: a caller cannot reach the backing array through it, so the
    ///     cached token total can never disagree with the entries it was computed from.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> Entries => _entriesView;

    /// <summary>
    ///     Gets the estimated tokens every entry in this transcript occupies together.
    /// </summary>
    public int EstimatedTokens { get; }

    /// <summary>
    ///     Returns a transcript with one more entry at the end.
    /// </summary>
    /// <param name="entry">The entry to append. Must not be <see langword="null"/>.</param>
    /// <returns>A new transcript; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public SessionTranscript Append(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var appended = new TranscriptEntry[_entries.Length + 1];
        _entries.CopyTo(appended, 0);
        appended[^1] = entry;
        return new SessionTranscript(appended);
    }

    /// <summary>
    ///     Returns a transcript with several more entries at the end, in order.
    /// </summary>
    /// <remarks>
    ///     Offered alongside the single-entry append because one turn of a tool-using agent
    ///     produces a run of entries — an assistant message, then call and result pairs — and
    ///     appending them one at a time would allocate a new array for each.
    /// </remarks>
    /// <param name="entries">
    ///     The entries to append, in order. Must not be <see langword="null"/> and must contain no
    ///     <see langword="null"/> entry. An empty sequence returns this transcript unchanged.
    /// </param>
    /// <returns>A new transcript, or this one when <paramref name="entries"/> is empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entries"/> contains a <see langword="null"/> entry.</exception>
    public SessionTranscript Append(IEnumerable<TranscriptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var added = entries as IReadOnlyList<TranscriptEntry> ?? [.. entries];
        if (added.Count == 0)
        {
            return this;
        }

        var appended = new TranscriptEntry[_entries.Length + added.Count];
        _entries.CopyTo(appended, 0);
        for (var index = 0; index < added.Count; index++)
        {
            appended[_entries.Length + index] = added[index]
                ?? throw new ArgumentException("An entry in the sequence is null.", nameof(entries));
        }

        return new SessionTranscript(appended);
    }

    /// <summary>
    ///     Splits this transcript into the newest entries that fit a verbatim budget and the older
    ///     entries that must be consolidated, never separating a tool call from its result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Newest-first, because recency is what tier zero is for.</b> The walk starts at the
    ///     end and accumulates backwards until the next entry would not fit, so the retained set is
    ///     always a contiguous suffix — the most recent turns, held verbatim.
    ///     </para>
    ///     <para>
    ///     <b>Tool call and result pairs are indivisible, so the boundary snaps.</b> A retained set
    ///     holding a tool result whose call went into the overflow is an orphan: some providers
    ///     reject that outright, and a model presented with it cannot tell what was asked. The
    ///     boundary therefore moves <em>later</em> — past any result whose call is not also
    ///     retained — rather than earlier. That is checked across the whole retained window, not
    ///     merely at its first entry, because an interleaved turn such as <c>call c1, call c2,
    ///     result c1, result c2</c> can put the boundary on c2's call and strand c1's result behind
    ///     it. Moving later can only shrink the retained set, so snapping can never push it back
    ///     over budget, whereas moving earlier to recover the call could.
    ///     </para>
    ///     <para>
    ///     <b>An entry larger than the whole budget retains nothing.</b> That is reported honestly
    ///     rather than papered over by retaining it anyway: an oversized entry that cannot fit tier
    ///     zero is consolidated like any other overflow, and the caller sees an empty retained set.
    ///     The same is true of an interleaved run with no orphan-free suffix inside the budget —
    ///     the whole run is consolidated together, which is the only split that keeps every pair
    ///     intact.
    ///     </para>
    /// </remarks>
    /// <param name="budgetTokens">
    ///     The verbatim token budget the retained entries must fit within. Must not be negative;
    ///     zero retains nothing.
    /// </param>
    /// <returns>
    ///     The retained newest entries as a transcript, and the older entries to consolidate,
    ///     oldest first.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budgetTokens"/> is negative.</exception>
    public (SessionTranscript Retained, IReadOnlyList<TranscriptEntry> Overflow) SplitAtBudget(int budgetTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(budgetTokens);

        // Walk backwards from the newest entry, accumulating until the next one would not fit.
        // 'first' ends as the index of the oldest retained entry.
        var used = 0;
        var first = _entries.Length;
        while (first > 0)
        {
            var candidate = _entries[first - 1];
            if (used + candidate.EstimatedTokens > budgetTokens)
            {
                break;
            }

            used += candidate.EstimatedTokens;
            first--;
        }

        // Snap the boundary later so the retained set never holds a tool result whose call was
        // consolidated away. Checking only the first retained entry is not enough: a valid
        // interleaved turn - call c1, call c2, result c1, result c2 - can put the boundary on
        // c2's call, which is not a result and so passes that check while c1's result stays
        // retained with its call in the overflow. Parallel tool calls are ordinary agent traffic,
        // so the whole retained window is validated instead.
        //
        // Moving later only removes entries, so this can never exceed the budget just satisfied.
        // It repeats because moving past an orphan also drops the calls before it, which can
        // orphan a result that was paired a moment ago.
        int orphan;
        while ((orphan = FirstOrphanedResult(first)) >= 0)
        {
            first = orphan + 1;
        }

        // Nothing overflowed: hand back this very transcript rather than an equal copy.
        if (first == 0)
        {
            return (this, []);
        }

        return (new SessionTranscript(_entries[first..]), _entries[..first]);
    }

    /// <summary>
    ///     Finds the first entry in a candidate retained window that is a tool result with no
    ///     matching call inside that same window.
    /// </summary>
    /// <remarks>
    ///     The match must be a call <em>earlier</em> in the window, because a result recorded
    ///     before the call it answers is not a pairing a provider would accept either. Scanning
    ///     forward with the calls seen so far is what makes an interleaved run — several calls
    ///     issued together, their results arriving afterwards in any order — resolve correctly.
    /// </remarks>
    /// <param name="first">The index the retained window would begin at.</param>
    /// <returns>The index of the first orphaned result, or -1 when every retained result is paired.</returns>
    private int FirstOrphanedResult(int first)
    {
        HashSet<string>? calls = null;
        for (var index = first; index < _entries.Length; index++)
        {
            var entry = _entries[index];

            if (entry.Kind == TranscriptEntryKind.ToolCall)
            {
                calls ??= new HashSet<string>(StringComparer.Ordinal);
                calls.Add(entry.ToolCallId!);
                continue;
            }

            if (entry.Kind == TranscriptEntryKind.ToolResult
                && calls?.Contains(entry.ToolCallId!) != true)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Renders a run of entries as the labeled text a stateless summarizer is handed.
    /// </summary>
    /// <remarks>
    ///     Static and deterministic: the same entries always render to the same string, which is
    ///     what allows a fake summarizer in a test to assert on exactly what the engine asked it to
    ///     consolidate.
    /// </remarks>
    /// <param name="entries">
    ///     The entries to render, oldest first. Must not be <see langword="null"/>. An empty
    ///     sequence renders as an empty string.
    /// </param>
    /// <returns>One labeled line per entry, separated by newlines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public static string Render(IEnumerable<TranscriptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return string.Join("\n", entries.Select(entry => entry.ToTranscriptLine()));
    }
}
