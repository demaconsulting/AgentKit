using System.Collections.ObjectModel;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     What one transcript entry is, which decides how it is framed for a provider.
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
///     One item of session history, as the engine records it out of session.
/// </summary>
/// <remarks>
///     <para>
///     <b>The currency of the provider seam.</b> A <see cref="ProviderSessionSeed"/> carries these
///     as the history a fresh provider session resumes from, and a <see cref="ProviderTurn"/>
///     returns these as what a turn produced. An adapter maps its provider's own message shape to
///     and from them, and needs nothing else from this library to do so.
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
    ///     lets a provider match the outcome to the request, and an unidentified call could not be
    ///     paired. An identifier on a plain message would be meaningless and is refused rather than
    ///     ignored, so a caller that supplies one learns it misunderstood the model.
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
        // agent is seeded from. Refused here.
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "The entry kind must be a defined TranscriptEntryKind member.");
        }

        // Validate the pairing identifier before any assignment, so an entry that could not be
        // paired never exists even briefly.
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
    ///     Computed once at construction because the entry is immutable. It is a fallback estimate,
    ///     used when a provider reports no usage of its own and when the session sizes a seed it has
    ///     not yet sent.
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
///     One turn of a session: the message that was sent, the answer, and every tool call and tool
///     result produced in between, kept together as a single indivisible exchange.
/// </summary>
/// <remarks>
///     <para>
///     <b>A turn is the unit of the verbatim tail and the granularity of every boundary.</b> The
///     round-robin structure keeps a number of whole turns word for word and consolidates whole
///     turns; nothing is ever split partway through one. Grouping the tool traffic with the answer
///     it belongs to is what retires the old rule that had to snap a boundary so a tool call was
///     never separated from its result — with a turn as the unit, they cannot be separated because
///     they are one thing.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class SessionTurn
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SessionTurn"/> class.
    /// </summary>
    /// <param name="entries">The entries of one turn, in order. Ownership passes to this instance.</param>
    internal SessionTurn(TranscriptEntry[] entries)
    {
        Entries = Array.AsReadOnly(entries);

        var total = 0;
        foreach (var entry in entries)
        {
            total += entry.EstimatedTokens;
        }

        EstimatedTokens = total;
    }

    /// <summary>
    ///     Gets the entries this turn is made of, in order.
    /// </summary>
    public IReadOnlyList<TranscriptEntry> Entries { get; }

    /// <summary>
    ///     Gets the estimated tokens this turn occupies, including per-entry framing.
    /// </summary>
    public int EstimatedTokens { get; }
}

/// <summary>
///     The append-only verbatim history the engine keeps out of session, grouped into whole turns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Append-only between rotations, by design.</b> Nothing already sent to a provider is ever
///     rewritten while a session is live. That is what preserves prompt caching: a provider that
///     recognizes an unchanged prefix charges less for it, and an in-place edit anywhere in the
///     history invalidates that prefix for every following turn. All reshaping happens in one batch
///     at rotation, where the cache is invalidated anyway because the session is being replaced.
///     </para>
///     <para>
///     <b>Turn-granular.</b> The transcript holds a list of whole turns. The verbatim tail is the
///     newest turns; everything older is what a rotation consolidates. Both are counted in turns,
///     never tokens, which is what lets the compaction structure be configured in counts.
///     </para>
///     <para>
///     Instances are immutable: every mutator returns a new transcript. That makes the rotation
///     engine a pure function of its inputs and lets a test compare a before and after. Instances
///     are safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class SessionTranscript
{
    /// <summary>
    ///     The turns, oldest first.
    /// </summary>
    private readonly SessionTurn[] _turns;

    /// <summary>
    ///     The read-only view handed out by <see cref="Turns"/>.
    /// </summary>
    private readonly ReadOnlyCollection<SessionTurn> _turnsView;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SessionTranscript"/> class.
    /// </summary>
    /// <param name="turns">The turns, oldest first. Ownership passes to this instance.</param>
    private SessionTranscript(SessionTurn[] turns)
    {
        _turns = turns;
        _turnsView = Array.AsReadOnly(turns);

        var total = 0;
        foreach (var turn in turns)
        {
            total += turn.EstimatedTokens;
        }

        EstimatedTokens = total;
    }

    /// <summary>
    ///     Gets the transcript holding no turns.
    /// </summary>
    public static SessionTranscript Empty { get; } = new([]);

    /// <summary>
    ///     Gets the turns, oldest first.
    /// </summary>
    public IReadOnlyList<SessionTurn> Turns => _turnsView;

    /// <summary>
    ///     Gets the number of turns held verbatim.
    /// </summary>
    public int TurnCount => _turns.Length;

    /// <summary>
    ///     Gets the estimated tokens every turn in this transcript occupies together.
    /// </summary>
    public int EstimatedTokens { get; }

    /// <summary>
    ///     Gets every entry of every turn, oldest first.
    /// </summary>
    /// <remarks>
    ///     The flattened view a fresh provider session is seeded with as verbatim history, and the
    ///     material a rotation renders for the summarizer.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> Entries
    {
        get
        {
            var entries = new List<TranscriptEntry>();
            foreach (var turn in _turns)
            {
                entries.AddRange(turn.Entries);
            }

            return entries;
        }
    }

    /// <summary>
    ///     Returns a transcript with one more turn at the end.
    /// </summary>
    /// <remarks>
    ///     The entries of one exchange — the message, the answer, and any tool traffic between them
    ///     — are grouped as a single turn, so the boundary machinery only ever deals in whole
    ///     exchanges.
    /// </remarks>
    /// <param name="entries">
    ///     The entries of the turn, in order. Must not be <see langword="null"/>, must contain no
    ///     <see langword="null"/> entry, and must not be empty.
    /// </param>
    /// <returns>A new transcript; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="entries"/> is empty or contains a <see langword="null"/> entry.
    /// </exception>
    public SessionTranscript AppendTurn(IEnumerable<TranscriptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var turnEntries = entries as IReadOnlyList<TranscriptEntry> ?? [.. entries];
        if (turnEntries.Count == 0)
        {
            throw new ArgumentException("A turn must hold at least one entry.", nameof(entries));
        }

        var copied = new TranscriptEntry[turnEntries.Count];
        for (var index = 0; index < turnEntries.Count; index++)
        {
            copied[index] = turnEntries[index]
                ?? throw new ArgumentException("An entry in the turn is null.", nameof(entries));
        }

        var appended = new SessionTurn[_turns.Length + 1];
        _turns.CopyTo(appended, 0);
        appended[^1] = new SessionTurn(copied);
        return new SessionTranscript(appended);
    }

    /// <summary>
    ///     Splits the transcript into the entries of every turn older than the last
    ///     <paramref name="keepTurns"/>, and the transcript of those newest turns held verbatim.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The boundary is turn-granular: the newest <paramref name="keepTurns"/> whole turns are
    ///     retained, and everything older is returned as rendered material for a single
    ///     consolidation. A tool call is never separated from its result, because a turn holds both.
    ///     </para>
    ///     <para>
    ///     When the transcript holds no more than <paramref name="keepTurns"/> turns there is
    ///     nothing older, so the older set is empty and the whole transcript is retained.
    ///     </para>
    /// </remarks>
    /// <param name="keepTurns">The number of newest turns to keep verbatim. Must not be negative.</param>
    /// <returns>
    ///     The older turns, oldest first, and the retained newest turns as a transcript.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keepTurns"/> is negative.</exception>
    public (IReadOnlyList<SessionTurn> Older, SessionTranscript Retained) SplitAtTail(int keepTurns)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepTurns);

        if (_turns.Length <= keepTurns)
        {
            return ([], this);
        }

        var boundary = _turns.Length - keepTurns;

        // Whole turns, not their flattened entries. A turn is the indivisible unit here: consolidation
        // chunks large material into several summarizer calls, and handing that grouping a flat run of
        // entries lets it split one turn's message, tool call, tool result and answer across separate
        // calls - presenting a result whose call is in another chunk, which is exactly the orphaning
        // that turn-granular boundaries exist to make impossible.
        return (_turns[..boundary], new SessionTranscript(_turns[boundary..]));
    }

    /// <summary>
    ///     Returns a transcript with the oldest turn removed.
    /// </summary>
    /// <remarks>
    ///     The last resort under sustained pressure: when the context keeps filling and no
    ///     consolidated slot remains to bin, the oldest verbatim turn is discarded instead. Reached
    ///     only when consolidation is failing outright - a summarizer answering blank leaves its
    ///     material where it is, so no slot is ever written and there is nothing else left to shed.
    ///     The caller never drops the last turn: a session must be able to answer the message it was
    ///     just given.
    /// </remarks>
    /// <returns>A new transcript without its oldest turn; this one is unchanged.</returns>
    /// <exception cref="InvalidOperationException">The transcript holds no turns to drop.</exception>
    public SessionTranscript DropOldestTurn()
    {
        if (_turns.Length == 0)
        {
            throw new InvalidOperationException("The transcript holds no turns to drop.");
        }

        return new SessionTranscript(_turns[1..]);
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
    ///     The entries to render, oldest first. Must not be <see langword="null"/> and must contain
    ///     no <see langword="null"/> entry. An empty sequence renders as an empty string.
    /// </param>
    /// <returns>One labeled line per entry, separated by newlines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entries"/> contains a <see langword="null"/> entry.</exception>
    public static string Render(IEnumerable<TranscriptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rendered = entries as IReadOnlyList<TranscriptEntry> ?? [.. entries];
        var lines = new string[rendered.Count];
        for (var index = 0; index < rendered.Count; index++)
        {
            var entry = rendered[index]
                ?? throw new ArgumentException("An entry in the sequence is null.", nameof(entries));

            lines[index] = entry.ToTranscriptLine();
        }

        return string.Join("\n", lines);
    }
}
