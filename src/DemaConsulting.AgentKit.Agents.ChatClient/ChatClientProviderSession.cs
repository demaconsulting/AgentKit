// Copyright (c) DEMA Consulting
// SPDX-License-Identifier: MIT

using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     A provider session over any <see cref="IChatClient"/>, for providers that hold no
///     conversation of their own.
/// </summary>
/// <remarks>
///     <para>
///     <b>One implementation for the whole stateless family.</b> Ollama, OpenAI and AI Foundry all
///     reach this library as an <see cref="IChatClient"/>, and none of them keeps the conversation:
///     every turn sends the whole message list again. Seeding a session is therefore starting a list,
///     and releasing one is forgetting it. Writing that once means the mapping between this
///     library's transcript entries and chat messages - the part most likely to rot, because
///     tool-calling shapes differ between providers - exists in a single place.
///     </para>
///     <para>
///     <b>The window is supplied, because only the caller can know it.</b> An
///     <see cref="IChatClient"/> publishes no context window: the abstraction exposes a provider
///     name, a provider URI and a default model identifier, and nothing about limits. One layer
///     down the number is nearly always available - Ollama reports the loaded model's context
///     length and takes a configured one, and for a hosted model the window is a published property
///     of the model an application has already chosen. So it is asked for here, once, where the
///     provider is configured, rather than guessed at by this library or defaulted to a number that
///     would be wrong for most callers.
///     </para>
///     <para>
///     <b>Usage comes from the provider, because it is the only honest source - but from the last
///     prompt, not from the turn's total.</b> A tool-using turn is several requests, and the
///     <see cref="ChatResponse"/> the tool-invoking layer finally returns carries usage summed
///     across all of them. The occupancy a rotation decision needs is the prompt of the
///     <em>last</em> request, because by then the tool calls and their results are part of the
///     conversation being sent; a <see cref="PromptSizeRecordingChatClient"/> installed beneath that
///     layer sees each request separately and holds that figure. Every provider reached this way
///     reports usage. One that never does is refused rather than estimated around: knowing when the
///     window is filling is the one thing this library needs a token count for, and guessing at it
///     would mean guessing forever at the single fact the whole arrangement turns on.
///     </para>
///     <para>
///     <b>A session is obtained from <see cref="ChatClientProviderSessionFactory"/>.</b> That
///     factory builds the pipeline this session runs on, and is the only thing that can: a client
///     without the recorder beneath it reports no prompt size, and one without the tool-invoking
///     layer above it emits tool calls nothing answers.
///     </para>
///     <para>
///     Instances are not safe for concurrent use, consistent with <see cref="IProviderSession"/>.
///     </para>
/// </remarks>
public sealed class ChatClientProviderSession : IProviderSession
{
    /// <summary>
    ///     The label introducing a seeded tool result, so the model reads it as an answer a tool
    ///     gave rather than as something the assistant asserted.
    /// </summary>
    private const string ToolResultPrefix = "Tool result: ";

    /// <summary>
    ///     The client carrying each turn.
    /// </summary>
    private readonly IChatClient _client;

    /// <summary>
    ///     Sees each request a turn makes, so occupancy is the last prompt rather than their sum.
    /// </summary>
    private readonly PromptSizeRecordingChatClient _recorder;

    /// <summary>
    ///     The options every turn is sent with, carrying the tools the agent may call.
    /// </summary>
    private readonly ChatOptions? _options;

    /// <summary>
    ///     The whole conversation, resent on every turn because the provider holds none of it.
    /// </summary>
    private readonly List<ChatMessage> _messages = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientProviderSession"/> class.
    /// </summary>
    /// <param name="client">The chat client carrying each turn. Must not be <see langword="null"/>.</param>
    /// <param name="recorder">Sees each request a turn makes, underneath tool invocation.</param>
    /// <param name="seed">What the session starts from. Must not be <see langword="null"/>.</param>
    /// <param name="windowTokens">
    ///     The provider's context window in tokens. Must be positive. Read it from the provider
    ///     where it can be - Ollama publishes the loaded model's context length - or state the
    ///     window of the model the application chose.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="seed"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowTokens"/> is not positive.</exception>
    internal ChatClientProviderSession(
        IChatClient client,
        PromptSizeRecordingChatClient recorder,
        ProviderSessionSeed seed,
        int windowTokens)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        _client = client;
        _recorder = recorder;
        WindowTokens = windowTokens;

        if (!string.IsNullOrEmpty(seed.Instructions))
        {
            _messages.Add(new ChatMessage(ChatRole.System, seed.Instructions));
        }

        foreach (var entry in seed.History)
        {
            _messages.Add(ToChatMessage(entry));
        }

        if (seed.Tools.Count > 0)
        {
            _options = new ChatOptions { Tools = [.. seed.Tools] };
        }
    }

    /// <summary>
    ///     Gets the provider's context window in tokens, as supplied at construction.
    /// </summary>
    public int WindowTokens { get; }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     The provider's own count of the last prompt it was sent, which is exactly the occupancy a
    ///     rotation decision needs, taken against the window this session was given. Deliberately not
    ///     the total a tool-using turn is billed for, which is larger than the conversation ever was.
    ///     </para>
    ///     <para>
    ///     <b>Before the first turn it is zero, because nothing has been sent.</b> A stateless
    ///     provider receives the conversation with the request, so a session that has not sent one
    ///     occupies nothing of the provider's window - including immediately after a rotation, when
    ///     the replacement holds a seed the provider has not seen yet.
    ///     </para>
    ///     <para>
    ///     <b>A provider that answers without reporting usage is refused rather than guessed at.</b>
    ///     Knowing when the window is filling is the one thing this library needs a token count for,
    ///     and estimating it would mean quietly guessing forever at the single fact the whole
    ///     arrangement turns on. Every provider reached this way reports it - Ollama returns its
    ///     prompt evaluation count, and the hosted services report usage with every response - so an
    ///     absent figure means something is wrong that the application can see and fix, and saying so
    ///     is more use than a number nobody can trust.
    ///     </para>
    /// </remarks>
    public ContextUsage CurrentUsage
    {
        get
        {
            if (_recorder.LastPromptTokens is not { } reported)
            {
                return ContextUsage.FromProvider(0, WindowTokens, 0);
            }

            // Reported as it came back, including past the window. A provider that has overrun its
            // own limit is the condition an application most needs to see, and clamping it to the
            // window would report a session at exactly full whether it had overrun by forty tokens
            // or by forty thousand. The rotation decision is the same either way; the reporting is
            // not.
            var used = (int)Math.Min(reported, int.MaxValue);
            return ContextUsage.FromProvider(used, WindowTokens, used);
        }
    }

    /// <inheritdoc/>
    public async Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsReleased, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Answer first, record second. The turn is sent as this conversation plus the new message
        // without either joining it, so a provider that fails, times out or is cancelled mid-flight
        // leaves this session exactly as it was - which is what the session contract promises a
        // caller, and what the in-memory provider models. Appending before the call leaves a user
        // message with no answer beside it, which the next turn would send again.
        var outgoing = new List<ChatMessage>(_messages) { new(ChatRole.User, message) };

        // Cleared so the refusal below means what it says: that no request of *this* turn reported
        // usage. Left standing, a provider that reported once and then stopped would freeze
        // occupancy at that first figure, never reach the rotation threshold again, and grow the
        // conversation without limit.
        _recorder.Forget();

        var response = await _client.GetResponseAsync(outgoing, _options, cancellationToken)
            .ConfigureAwait(false);

        // The turn is answered; the recorder underneath has the prompt size of the last request it
        // took to get there. A turn that reported none leaves nothing to rotate on, which is refused
        // rather than guessed at - and refused before anything is recorded, so the session is
        // unchanged.
        if (_recorder.LastPromptTokens is null)
        {
            throw new InvalidOperationException(
                "The provider answered without reporting token usage, so this session cannot tell "
                + "when its context window is filling. Use a chat client that reports usage, or "
                + "wrap this one in an implementation that does.");
        }

        // The turn stands: the message and everything the provider produced join the conversation
        // together, so the next turn sends a history matching what the provider has already seen.
        _messages.AddRange(outgoing[^1..]);
        _messages.AddRange(response.Messages);

        var entries = new List<TranscriptEntry>();
        foreach (var produced in response.Messages)
        {
            entries.AddRange(ToTranscriptEntries(produced));
        }

        return new ProviderTurn(response.Text, entries);
    }

    /// <summary>
    ///     Gets a value indicating whether this session has been released.
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     The provider holds nothing to release, so this forgets the conversation and marks the
    ///     session spent. The client itself is not disposed: it was supplied by the caller, is
    ///     shared by every session a rotation creates, and outlives all of them.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        IsReleased = true;
        _messages.Clear();
        return default;
    }

    /// <summary>
    ///     Renders one transcript entry as the chat message a provider expects.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A tool call and its result are carried as labeled assistant text rather than as
    ///     function-call content, because a seeded history is a record of what happened rather than
    ///     a live exchange: the call has already been answered, and replaying it as a pending call
    ///     invites a provider to answer it again. A rotation can also separate a call from its
    ///     result, and a call replayed as function-call content with no result behind it is exactly
    ///     the pending call this avoids.
    ///     </para>
    ///     <para>
    ///     A result is not carried on a tool-role message either, even though that reads as the
    ///     natural home for it. A tool-role message is addressed by call identifier on the wire, so
    ///     one carrying only text cannot be represented: the OpenAI family maps tool-role content to
    ///     a message only when it is <c>FunctionResultContent</c>, and drops anything else without
    ///     an error. The result would vanish from the seed after every rotation, leaving the model a
    ///     conversation in which it called a tool and was never told the answer. Labeled text is
    ///     understood by every provider and can be dropped by none.
    ///     </para>
    /// </remarks>
    /// <param name="entry">The entry to render.</param>
    /// <returns>The chat message carrying it.</returns>
    private static ChatMessage ToChatMessage(TranscriptEntry entry) => entry.Kind switch
    {
        TranscriptEntryKind.UserMessage => new ChatMessage(ChatRole.User, entry.Text),
        TranscriptEntryKind.ToolResult => new ChatMessage(ChatRole.Assistant, $"{ToolResultPrefix}{entry.Text}"),
        _ => new ChatMessage(ChatRole.Assistant, entry.Text),
    };

    /// <summary>
    ///     Records what one message a provider produced contributed to the history.
    /// </summary>
    /// <remarks>
    ///     Tool calls and their results are recorded as the pairs this library's transcript expects,
    ///     carrying the provider's own call identifier so a rotation can keep them together. A
    ///     message holding neither is recorded as the assistant text it is.
    /// </remarks>
    /// <param name="message">The message the provider produced.</param>
    /// <returns>The transcript entries it contributes, in order.</returns>
    private static IEnumerable<TranscriptEntry> ToTranscriptEntries(ChatMessage message)
    {
        var recorded = false;

        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    recorded = true;
                    yield return TranscriptEntry.ToolCall(
                        call.CallId,
                        $"{call.Name}({FormatArguments(call.Arguments)})");
                    break;

                case FunctionResultContent result:
                    recorded = true;
                    yield return TranscriptEntry.ToolResult(
                        result.CallId,
                        result.Result?.ToString() ?? string.Empty);
                    break;

                default:
                    break;
            }
        }

        // Only when the message carried no tool traffic, so an answer delivered alongside a call is
        // not recorded twice.
        if (!recorded && !string.IsNullOrEmpty(message.Text))
        {
            yield return TranscriptEntry.Assistant(message.Text);
        }
    }

    /// <summary>
    ///     Renders a tool call's arguments compactly for the transcript.
    /// </summary>
    /// <remarks>
    ///     The transcript is read by a summarizer rather than executed, so the arguments are
    ///     recorded for what they say about the call rather than to be parsed back.
    /// </remarks>
    /// <param name="arguments">The arguments, or <see langword="null"/> when the call took none.</param>
    /// <returns>The rendered arguments.</returns>
    private static string FormatArguments(IDictionary<string, object?>? arguments) =>
        arguments is null || arguments.Count == 0
            ? string.Empty
            : string.Join(", ", arguments.Select(pair => $"{pair.Key}: {pair.Value}"));
}
