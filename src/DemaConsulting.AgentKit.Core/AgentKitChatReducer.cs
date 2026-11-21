using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     A <see cref="IChatReducer"/> that consolidates the older part of a host-owned chat history
///     into one record through AgentKit's <see cref="ISummarizer"/> and
///     <see cref="ConsolidationPrompt"/>, keeping system messages and a verbatim tail of whole turns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why it exists.</b> A host that owns its chat history — a Microsoft Agent Framework
///     <c>ChatClientAgent</c> whose <c>InMemoryChatHistoryProvider</c> takes a
///     <see cref="IChatReducer"/>, or a raw <see cref="IChatClient"/> pipeline with a reducing client
///     — can plug this reducer into the hook it already has and get AgentKit's consolidation prompt,
///     which preserves named facts, decisions, errors and outstanding work, without adopting
///     <see cref="CompactingAgentSession"/>.
///     </para>
///     <para>
///     <b>What it does.</b> When the estimated size of the history exceeds
///     <see cref="ThresholdCharacters"/>, every message older than the last
///     <see cref="VerbatimTurns"/> turns is rendered as labeled transcript lines — a previous record
///     included, so records fold — and consolidated by the summarizer at <see cref="Level"/>. The
///     result is the system messages, then one assistant message opening with
///     <see cref="RecordHeader"/> and holding the record, then the verbatim tail. A turn starts at a
///     user message, so a tool call is never separated from its result. Image and binary content is
///     rendered as its media type and size, never as bytes. At or under the threshold, or when no
///     message precedes the tail, the history is returned unchanged.
///     </para>
///     <para>
///     <b>What it does not do, compared with <see cref="CompactingAgentSession"/>.</b> It keeps one
///     record, re-consolidated each time (a record of a record), rather than tiers with a verbatim
///     tier zero and coarser older tiers. It runs at one fixed <see cref="CompactionLevel"/> with no
///     occupancy feedback or hysteresis, so a reduction can even make the history larger when the
///     record is longer than what it replaced. It never rotates into a fresh provider session; the
///     history is trimmed in place. It sees only messages, so size is an estimate in characters, not
///     the provider-reported token usage <see cref="ContextUsage"/> carries. It keeps no transcript
///     of what was consolidated, and it cannot help where the runtime owns the history — the GitHub
///     Copilot path has no reducer hook at all. Use <see cref="CompactingAgentSession"/> for Copilot
///     and wherever full fidelity matters.
///     </para>
///     <para>
///     The reducer holds no mutable state and is as safe for concurrent use as the summarizer it
///     wraps. It never modifies the messages it is handed.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Consolidating a history once it exceeds 40,000 characters, keeping the last four turns
///     verbatim.
///     </para>
///     <code>
///     ISummarizer summarizer = GetSummarizer(); // for example ChatClientSummarizer over any IChatClient
///     IChatReducer reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 40_000);
///
///     List&lt;ChatMessage&gt; history = LoadHistory();
///     history = [.. await reducer.ReduceAsync(history, CancellationToken.None)];
///
///     static ISummarizer GetSummarizer() => throw new NotImplementedException();
///     static List&lt;ChatMessage&gt; LoadHistory() => [];
///     </code>
/// </example>
public sealed class AgentKitChatReducer : IChatReducer
{
    /// <summary>
    ///     The number of most-recent turns kept verbatim when none is specified.
    /// </summary>
    public const int DefaultVerbatimTurns = 4;

    /// <summary>
    ///     The first line of the assistant message that carries a consolidated record.
    /// </summary>
    /// <remarks>
    ///     A later reduction recognizes a previous record by this header and folds it into the new
    ///     material as a record rather than as something the assistant said.
    /// </remarks>
    public const string RecordHeader = "[Consolidated record of the earlier conversation]";

    /// <summary>
    ///     The summarizer that writes each record.
    /// </summary>
    private readonly ISummarizer _summarizer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentKitChatReducer"/> class.
    /// </summary>
    /// <param name="summarizer">
    ///     The summarizer that writes each record. Must not be <see langword="null"/>. It runs out of
    ///     band, so it should not be the chat client whose history is being reduced.
    /// </param>
    /// <param name="thresholdCharacters">
    ///     The estimated history size, in characters, above which a reduction is attempted. Must be
    ///     greater than zero.
    /// </param>
    /// <param name="verbatimTurns">
    ///     The number of most-recent turns kept word for word. Must be one or greater.
    /// </param>
    /// <param name="level">
    ///     The compaction level whose aggressiveness clause every consolidation is asked for.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="summarizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="thresholdCharacters"/> is zero or less,
    ///     <paramref name="verbatimTurns"/> is less than one, or <paramref name="level"/> is not a
    ///     defined <see cref="CompactionLevel"/>.
    /// </exception>
    public AgentKitChatReducer(
        ISummarizer summarizer,
        int thresholdCharacters,
        int verbatimTurns = DefaultVerbatimTurns,
        CompactionLevel level = CompactionLevel.Low)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdCharacters);
        ArgumentOutOfRangeException.ThrowIfLessThan(verbatimTurns, 1);
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The level must be a defined CompactionLevel.");
        }

        _summarizer = summarizer;
        ThresholdCharacters = thresholdCharacters;
        VerbatimTurns = verbatimTurns;
        Level = level;
    }

    /// <summary>
    ///     Gets the estimated history size, in characters, above which a reduction is attempted.
    /// </summary>
    public int ThresholdCharacters { get; }

    /// <summary>
    ///     Gets the number of most-recent turns kept word for word.
    /// </summary>
    public int VerbatimTurns { get; }

    /// <summary>
    ///     Gets the compaction level whose aggressiveness clause every consolidation is asked for.
    /// </summary>
    public CompactionLevel Level { get; }

    /// <summary>
    ///     Reduces a history by consolidating everything older than the verbatim tail into one record.
    /// </summary>
    /// <remarks>
    ///     The input is enumerated once. When no reduction is made the materialized messages are
    ///     returned in their original order; the message instances are never modified.
    /// </remarks>
    /// <param name="messages">The history to reduce. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reduction.</param>
    /// <returns>
    ///     The system messages, the record and the verbatim tail; or the history unchanged when it is
    ///     within the threshold, has nothing older than the tail, or the summarizer returned a blank
    ///     record.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="messages"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the summarizer returns <see langword="null"/>, which its contract forbids.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled before or during the
    ///     consolidation.
    /// </exception>
    public async Task<IEnumerable<ChatMessage>> ReduceAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        // Materialize once: a history that fits is returned as it is, at no further cost.
        var history = messages.ToList();
        if (history.Sum(Measure) <= ThresholdCharacters)
        {
            return history;
        }

        // System messages are instructions, not history; they are kept whole and first.
        var system = history.Where(message => message.Role == ChatRole.System).ToList();
        var conversation = history.Where(message => message.Role != ChatRole.System).ToList();

        // The tail begins at the user message that opens the oldest kept turn, so a tool call and
        // its result, which always follow their user message, are never separated.
        var tailStart = TailStart(conversation);
        var older = conversation.Take(tailStart).ToList();

        // Consolidating nothing new — no message, or only a previous record — buys nothing.
        if (older.All(IsRecord))
        {
            return history;
        }

        var material = string.Join("\n", older.SelectMany(Render).Select(entry => entry.ToTranscriptLine()));
        if (string.IsNullOrWhiteSpace(material))
        {
            return history;
        }

        var record = await _summarizer
            .ConsolidateAsync(
                new ConsolidationRequest(1, material, ConsolidationPrompt.InstructionFor(Level)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // A null record breaks the summarizer's contract and is reported, as the rotation engine
        // reports it; a blank one is a refusal, and the material is kept rather than lost.
        if (record is null)
        {
            throw new InvalidOperationException(
                "The summarizer returned null; an implementation with nothing to say must return an empty string.");
        }

        if (string.IsNullOrWhiteSpace(record))
        {
            return history;
        }

        return [.. system, new ChatMessage(ChatRole.Assistant, RecordHeader + "\n" + record.Trim()), .. conversation.Skip(tailStart)];
    }

    /// <summary>
    ///     Finds where the verbatim tail begins.
    /// </summary>
    /// <param name="conversation">The non-system messages, in order.</param>
    /// <returns>
    ///     The index of the user message opening the oldest kept turn, or the index of the first turn
    ///     when there are no more turns than are kept.
    /// </returns>
    private int TailStart(List<ChatMessage> conversation)
    {
        var turnStarts = conversation
            .Select((message, index) => (message, index))
            .Where(item => item.message.Role == ChatRole.User)
            .Select(item => item.index)
            .ToList();

        if (turnStarts.Count == 0)
        {
            return 0;
        }

        return turnStarts.Count <= VerbatimTurns ? turnStarts[0] : turnStarts[^VerbatimTurns];
    }

    /// <summary>
    ///     Determines whether a message is a record an earlier reduction produced.
    /// </summary>
    /// <param name="message">The message to test.</param>
    /// <returns><see langword="true"/> for an assistant message opening with the record header.</returns>
    private static bool IsRecord(ChatMessage message) =>
        message.Role == ChatRole.Assistant
        && message.Text.StartsWith(RecordHeader, StringComparison.Ordinal);

    /// <summary>
    ///     Renders one message as the transcript entries the consolidation material is made of.
    /// </summary>
    /// <remarks>
    ///     A previous record becomes a context record, so records fold; tool calls and results keep
    ///     their identifier so the summarizer sees what answered what; image and binary content is
    ///     described by media type and size, never carried as bytes; content of any other kind
    ///     contributes nothing.
    /// </remarks>
    /// <param name="message">The message to render.</param>
    /// <returns>The entries, in order.</returns>
    private static IEnumerable<TranscriptEntry> Render(ChatMessage message)
    {
        if (IsRecord(message))
        {
            yield return TranscriptEntry.ContextRecord(message.Text[RecordHeader.Length..].Trim());
            yield break;
        }

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    yield return TranscriptEntry.ToolCall(CallId(call.CallId), call.Name + "(" + SerializeArguments(call) + ")");
                    break;

                case FunctionResultContent result:
                    yield return TranscriptEntry.ToolResult(CallId(result.CallId), DescribeValue(result.Result));
                    break;

                case TextContent or DataContent or UriContent:
                    var text = DescribeValue(content);
                    if (text.Length > 0)
                    {
                        yield return message.Role == ChatRole.User ? TranscriptEntry.User(text) : TranscriptEntry.Assistant(text);
                    }

                    break;
            }
        }
    }

    /// <summary>
    ///     Returns a call identifier usable in a transcript entry, which requires a non-blank one.
    /// </summary>
    /// <param name="callId">The identifier the content carried.</param>
    /// <returns>The identifier, or <c>unidentified</c> when it was blank.</returns>
    private static string CallId(string? callId) => string.IsNullOrWhiteSpace(callId) ? "unidentified" : callId;

    /// <summary>
    ///     Describes a value as the text a summarizer reads: text as itself, JSON as its text, binary
    ///     content as its media type and size, and anything else serialized to JSON.
    /// </summary>
    /// <param name="value">The value to describe; may be null.</param>
    /// <returns>The description; empty for a null value.</returns>
    private static string DescribeValue(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        TextContent text => text.Text,
        DataContent data => "[" + data.MediaType + ", "
                            + data.Data.Length.ToString(CultureInfo.InvariantCulture) + " bytes]",
        UriContent uri => "[" + uri.MediaType + ": " + uri.Uri + "]",
        JsonElement element => element.GetRawText(),
        IEnumerable<AIContent> parts => string.Join("\n", parts.Select(DescribeValue).Where(part => part.Length > 0)),
        AIContent => string.Empty,
        _ => SerializeOrDescribe(value)
    };

    /// <summary>
    ///     Estimates the characters one message occupies, counting binary content by its bytes.
    /// </summary>
    /// <param name="message">The message to measure.</param>
    /// <returns>The estimate.</returns>
    private static long Measure(ChatMessage message) => message.Contents.Sum(MeasureContent);

    /// <summary>
    ///     Estimates the characters one content item occupies.
    /// </summary>
    /// <param name="content">The content to measure.</param>
    /// <returns>The estimate.</returns>
    private static long MeasureContent(AIContent content) => content switch
    {
        TextContent text => text.Text.Length,
        DataContent data => data.Data.Length,
        FunctionCallContent call => call.Name.Length + SerializeArguments(call).Length,
        FunctionResultContent result => MeasureValue(result.Result),
        _ => 0
    };

    /// <summary>
    ///     Estimates the characters a function result's value occupies.
    /// </summary>
    /// <param name="value">The value; may be null.</param>
    /// <returns>The estimate.</returns>
    private static long MeasureValue(object? value) => value switch
    {
        AIContent content => MeasureContent(content),
        IEnumerable<AIContent> parts => parts.Sum(MeasureContent),
        _ => DescribeValue(value).Length
    };

    /// <summary>
    ///     Serializes a call's arguments to JSON, as the provider would see them.
    /// </summary>
    /// <param name="call">The call.</param>
    /// <returns>The JSON object text, or <c>{}</c> when there are none.</returns>
    private static string SerializeArguments(FunctionCallContent call) =>
        call.Arguments is null ? "{}" : SerializeOrDescribe(call.Arguments);

    /// <summary>
    ///     Serializes a value with the default AI serializer options, falling back to its string form
    ///     when it cannot be serialized.
    /// </summary>
    /// <remarks>
    ///     Material is best effort: a value the serializer cannot describe must not fail the
    ///     reduction, so its string form stands in for it.
    /// </remarks>
    /// <param name="value">The value to serialize. Must not be null.</param>
    /// <returns>The JSON text, or the value's string form.</returns>
    private static string SerializeOrDescribe(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType()));
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or JsonException)
        {
            return value.ToString() ?? string.Empty;
        }
    }
}
