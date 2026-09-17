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
///     <b>Usage comes from the provider, because it is the only honest source.</b>
///     <see cref="ChatResponse.Usage"/> carries the input tokens a provider counted for the request
///     it just answered, which is exactly the conversation occupancy a rotation decision needs.
///     Every provider reached this way reports it. One that answers without doing so is refused
///     rather than estimated around: knowing when the window is filling is the one thing this
///     library needs a token count for, and guessing at it would mean guessing forever at the single
///     fact the whole arrangement turns on.
///     </para>
///     <para>
///     Instances are not safe for concurrent use, consistent with <see cref="IProviderSession"/>.
///     </para>
/// </remarks>
public sealed class ChatClientProviderSession : IProviderSession
{
    /// <summary>
    ///     The client carrying each turn.
    /// </summary>
    private readonly IChatClient _client;

    /// <summary>
    ///     The options every turn is sent with, carrying the tools the agent may call.
    /// </summary>
    private readonly ChatOptions? _options;

    /// <summary>
    ///     The whole conversation, resent on every turn because the provider holds none of it.
    /// </summary>
    private readonly List<ChatMessage> _messages = [];

    /// <summary>
    ///     The provider's own count of the input it last answered, or <see langword="null"/> when it
    ///     reported none.
    /// </summary>
    private long? _reportedInputTokens;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientProviderSession"/> class.
    /// </summary>
    /// <param name="client">The chat client carrying each turn. Must not be <see langword="null"/>.</param>
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
    public ChatClientProviderSession(IChatClient client, ProviderSessionSeed seed, int windowTokens)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        _client = client;
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
    ///     The provider's own count of the input it last answered, which is exactly the occupancy a
    ///     rotation decision needs, taken against the window this session was given.
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
            if (_reportedInputTokens is not { } reported)
            {
                return ContextUsage.FromProvider(0, WindowTokens, 0);
            }

            // Clamped, because a provider that has already overrun its own window would otherwise
            // produce a figure the usage shape refuses. Being at the limit is the truthful reading
            // in that case, and it is the one that rotates.
            var used = (int)Math.Min(reported, WindowTokens);
            return ContextUsage.FromProvider(used, WindowTokens, used);
        }
    }

    /// <inheritdoc/>
    public async Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsReleased, this);
        cancellationToken.ThrowIfCancellationRequested();

        _messages.Add(new ChatMessage(ChatRole.User, message));

        var response = await _client.GetResponseAsync(_messages, _options, cancellationToken)
            .ConfigureAwait(false);

        // Everything the provider produced joins the conversation, so the next turn sends again a
        // history matching what it has already seen.
        _messages.AddRange(response.Messages);

        // Recorded before the entries are built, so a usage read between turns reflects the turn
        // that just happened rather than the one before it.
        _reportedInputTokens = response.Usage?.InputTokenCount
            ?? throw new InvalidOperationException(
                "The provider answered without reporting token usage, so this session cannot tell "
                + "when its context window is filling. Use a chat client that reports usage, or "
                + "wrap this one in an implementation that does.");

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
    ///     A tool call and its result are carried as assistant and tool messages rather than as
    ///     function-call content, because a seeded history is a record of what happened rather than
    ///     a live exchange: the call has already been answered, and replaying it as a pending call
    ///     invites a provider to answer it again.
    /// </remarks>
    /// <param name="entry">The entry to render.</param>
    /// <returns>The chat message carrying it.</returns>
    private static ChatMessage ToChatMessage(TranscriptEntry entry) => entry.Kind switch
    {
        TranscriptEntryKind.UserMessage => new ChatMessage(ChatRole.User, entry.Text),
        TranscriptEntryKind.ToolResult => new ChatMessage(ChatRole.Tool, entry.Text),
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
