using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Unit tests for <see cref="OllamaContextSizingChatClient"/>: that every request asks for the
///     context length the application chose, and that nothing else the caller set is disturbed.
/// </summary>
/// <remarks>
///     A window a session accounts against is only true if the instance is actually running at it,
///     and the only thing that makes that so is the size riding on each request. A decorator that
///     annotated the first request and not the hundredth would resize the instance mid-conversation
///     with nothing reporting it, so "every request" is tested rather than assumed.
/// </remarks>
public class OllamaContextSizingChatClientTests
{
    /// <summary>
    ///     The Ollama option naming the context length a model is loaded with.
    /// </summary>
    private const string ContextLengthOption = "num_ctx";

    /// <summary>
    ///     The context length these tests ask for.
    /// </summary>
    private const int ContextLength = 8192;

    /// <summary>
    ///     Proves the context length rides on every request, not merely the first.
    /// </summary>
    /// <remarks>
    ///     This is the promise the whole decorator exists for. Ollama reloads a model when a request
    ///     names a different context length, so a later un-annotated request would resize the
    ///     instance beneath a session still accounting against the old figure.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_EveryRequest_CarriesTheStatedContextLength()
    {
        // Arrange: a decorator over a client that records what each request carried
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);

        // Act: two successive requests, as a conversation makes
        await Send(client);
        await Send(client);

        // Assert: both requests asked for the size, not just the opening one
        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, options => Assert.Equal(ContextLength, ContextLengthOf(options)));
    }

    /// <summary>
    ///     Proves a request the caller gave no options for still asks for the context length.
    /// </summary>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_NoCallerOptions_StillCarriesTheContextLength()
    {
        // Arrange
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);

        // Act: no options at all, as a bare request carries
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            TestContext.Current.CancellationToken);

        // Assert: options were created to carry the size
        Assert.Equal(ContextLength, ContextLengthOf(Assert.Single(recorder.Requests)));
    }

    /// <summary>
    ///     Proves everything else the caller set reaches the provider alongside the context length.
    /// </summary>
    /// <remarks>
    ///     The decorator sits beneath a session that offers tools and chooses a temperature. Losing
    ///     either while adding the size would trade one silent failure for another.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_CallerOptions_ReachTheProviderUnchanged()
    {
        // Arrange: a caller that set options of its own, including one additional property
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);
        var callerOptions = new ChatOptions
        {
            Temperature = 0.25f,
            MaxOutputTokens = 512,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["keep_alive"] = "10m" },
        };

        // Act
        await Send(client, callerOptions);

        // Assert: the caller's settings survived, and the size was added beside them
        var forwarded = Assert.Single(recorder.Requests);
        Assert.Multiple(
            () => Assert.Equal(0.25f, forwarded?.Temperature),
            () => Assert.Equal(512, forwarded?.MaxOutputTokens),
            () => Assert.Equal("10m", forwarded?.AdditionalProperties?["keep_alive"]),
            () => Assert.Equal(ContextLength, ContextLengthOf(forwarded)));
    }

    /// <summary>
    ///     Proves the caller's own options object is left exactly as the caller built it.
    /// </summary>
    /// <remarks>
    ///     A session commonly builds one options instance and reuses it for every request, sharing
    ///     it with sibling clients. Writing the size into it would leak this decorator's choice to
    ///     clients that were never meant to carry it. The caller's options carry an additional
    ///     property of their own deliberately: a decorator that handed on the caller's own
    ///     additional-property dictionary would still look correct against a caller that had none,
    ///     because a dictionary is created either way.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_AnyRequest_DoesNotMutateTheCallersOptions()
    {
        // Arrange: options the caller will inspect afterwards, already holding a property of its own
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);
        var callerOptions = new ChatOptions
        {
            Temperature = 0.5f,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["keep_alive"] = "10m" },
        };

        // Act
        await Send(client, callerOptions);

        // Assert: the caller's instance and its own dictionary are as the caller built them, and
        // neither was the one forwarded
        var forwarded = Assert.Single(recorder.Requests);
        Assert.Multiple(
            () => Assert.Null(ContextLengthOf(callerOptions)),
            () => Assert.Equal(0.5f, callerOptions.Temperature),
            () => Assert.Equal("10m", callerOptions.AdditionalProperties["keep_alive"]),
            () => Assert.Single(callerOptions.AdditionalProperties),
            () => Assert.NotSame(callerOptions, forwarded),
            () => Assert.NotSame(callerOptions.AdditionalProperties, forwarded?.AdditionalProperties));
    }

    /// <summary>
    ///     Proves a context length the caller named is forwarded as the caller named it.
    /// </summary>
    /// <remarks>
    ///     A caller that set the option has already decided what the instance should run at.
    ///     Overruling it would leave that caller no way to say so at all.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_CallerSuppliedContextLength_IsNotOverwritten()
    {
        // Arrange: a caller naming a different size from the decorator's
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);
        var callerOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ContextLengthOption] = 1024 },
        };

        // Act
        await Send(client, callerOptions);

        // Assert: the caller's figure, not the decorator's
        Assert.Equal(1024, ContextLengthOf(Assert.Single(recorder.Requests)));
    }

    /// <summary>
    ///     Proves a streamed request asks for the size exactly as a whole-response request does.
    /// </summary>
    /// <remarks>
    ///     A host that streams would otherwise resize the instance on its first streamed turn, which
    ///     is the same silent failure by a different route.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetStreamingResponseAsync_EveryRequest_CarriesTheStatedContextLength()
    {
        // Arrange
        var recorder = new RecordingChatClient();
        using var client = new OllamaContextSizingChatClient(recorder, ContextLength);

        // Act: two streamed requests, each enumerated to completion
        await StreamAsync(client);
        await StreamAsync(client);

        // Assert
        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, options => Assert.Equal(ContextLength, ContextLengthOf(options)));
    }

    /// <summary>
    ///     Proves a missing inner client is refused where the composing application wrote it.
    /// </summary>
    [Fact]
    public void OllamaContextSizingChatClient_Constructor_NullInnerClient_ThrowsArgumentNullException()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new OllamaContextSizingChatClient(null!, ContextLength));
    }

    /// <summary>
    ///     Proves a context length no instance could run is refused at the composing line rather
    ///     than at some later request.
    /// </summary>
    /// <param name="contextLength">The unusable context length.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OllamaContextSizingChatClient_Constructor_NonPositiveContextLength_Throws(int contextLength)
    {
        // Arrange
        using var recorder = new RecordingChatClient();

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OllamaContextSizingChatClient(recorder, contextLength));
    }

    /// <summary>
    ///     Proves the size reaches the Ollama server on the wire, through the real client.
    /// </summary>
    /// <remarks>
    ///     This is the load-bearing claim beneath the whole change: a stated window is true by
    ///     construction only because the request really carries <c>num_ctx</c>, and AgentKit
    ///     implements none of the Ollama protocol itself. Asserting it against a hand-written fake
    ///     would assert this project's beliefs about OllamaSharp back at it, so the real
    ///     <c>OllamaApiClient</c> runs against a loopback server and the request body is read.
    /// </remarks>
    [Fact]
    public async Task OllamaContextSizingChatClient_GetResponseAsync_ThroughTheRealClient_SendsTheContextLengthOnTheWire()
    {
        // Arrange: a server answering a chat request, and the real client beneath the decorator
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/chat").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(ChatReply));
        using var ollama = new OllamaApiClient(new Uri(server.Url!), "qwen3.5:9b");
        using var client = new OllamaContextSizingChatClient(ollama, ContextLength);

        // Act
        await Send(client);

        // Assert: the server was asked to run the model at the chosen length
        var body = Assert.Single(server.LogEntries).RequestMessage?.Body;
        using var request = JsonDocument.Parse(body!);
        Assert.Equal(
            ContextLength,
            request.RootElement.GetProperty("options").GetProperty(ContextLengthOption).GetInt32());
    }

    /// <summary>
    ///     The reply a server sends to a whole-response chat request, enough for the client to
    ///     complete the exchange.
    /// </summary>
    private const string ChatReply =
        """{"model":"qwen3.5:9b","message":{"role":"assistant","content":"ok"},"done":true}""";

    /// <summary>
    ///     Sends one ordinary request through a client.
    /// </summary>
    /// <param name="client">The client to send through.</param>
    /// <param name="options">The options the caller sets, if any.</param>
    /// <returns>A task that completes when the request has been answered.</returns>
    private static Task<ChatResponse> Send(IChatClient client, ChatOptions? options = null) =>
        client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options,
            TestContext.Current.CancellationToken);

    /// <summary>
    ///     Sends one streamed request through a client and drains the updates.
    /// </summary>
    /// <remarks>
    ///     The sequence is enumerated because a streaming member does no work until it is, so an
    ///     un-drained call would record nothing.
    /// </remarks>
    /// <param name="client">The client to send through.</param>
    /// <returns>A task that completes when the stream has been drained.</returns>
    private static async Task StreamAsync(IChatClient client)
    {
        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            TestContext.Current.CancellationToken))
        {
            // Drained rather than inspected: the assertion is on what the request carried.
        }
    }

    /// <summary>
    ///     Reads the context length a set of options carries.
    /// </summary>
    /// <param name="options">The options to read, which may be <see langword="null"/>.</param>
    /// <returns>The context length, or <see langword="null"/> when the options name none.</returns>
    private static int? ContextLengthOf(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(ContextLengthOption, out var value) == true
            ? Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
}
