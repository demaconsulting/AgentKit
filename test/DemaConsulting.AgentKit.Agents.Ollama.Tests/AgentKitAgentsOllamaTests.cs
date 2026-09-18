using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     System-level tests for the AgentKit Ollama Agents package.
/// </summary>
/// <remarks>
///     The package makes one promise an application acts on: the window a conversation is accounted
///     against is the window the Ollama instance is actually using, with an honest account of where
///     that number came from. Two halves deliver it — asking the server to run at a chosen size, and
///     reporting what the running instance says — and both are exercised here as a consumer meets
///     them rather than one precedence step at a time.
/// </remarks>
public class AgentKitAgentsOllamaTests
{
    /// <summary>
    ///     Proves the package walks the whole precedence for one model and names the source of every
    ///     figure it reports.
    /// </summary>
    /// <remarks>
    ///     The three states are the ones a real server moves between over a conversation's life: the
    ///     model loaded and enforcing a length, the model not loaded and therefore describing no
    ///     instance, and an application that stated the size it asked the server to run at. An
    ///     application reads <c>Tokens</c> to size its session and <c>Source</c> to decide how much
    ///     to trust it, so both are asserted at every step.
    /// </remarks>
    [Fact]
    public void AgentKitAgentsOllama_ContextWindow_ReportsTheEnforcedWindowAndNamesItsSource()
    {
        // Arrange: one model, reported by a server as loaded at a length of the server's choosing
        const string model = "qwen3:8b";
        var loaded = new[]
        {
            new RunningModel { Name = model, ModelName = model, ContextLength = 8192 },
        };

        // Act: the same model, as the server's state changes over a conversation's life
        var enforced = OllamaContextWindow.Select(stated: null, loaded, model);
        var nothing = OllamaContextWindow.Select(stated: null, running: null, model);
        var asked = OllamaContextWindow.Select(32768, running: null, model);

        // Assert: the enforced length while loaded, the conservative default when no instance was
        // described, and the stated figure when the application asked for one - each named for
        // what it is
        Assert.Multiple(
            () => Assert.Equal(8192, enforced.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.LoadedModel, enforced.Source),
            () => Assert.Equal(OllamaContextWindow.AssumedTokens, nothing.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.Assumed, nothing.Source),
            () => Assert.Equal(32768, asked.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.Stated, asked.Source));
    }

    /// <summary>
    ///     Proves the figure reported as stated is the figure every request asks the server for.
    /// </summary>
    /// <remarks>
    ///     This is what makes a stated window true rather than merely claimed. One number chosen by
    ///     the application is used twice — handed to the session that accounts against it, and put
    ///     on every request the conversation sends — and the two must not be allowed to drift apart,
    ///     because a drift would resize the instance beneath a session that reports no error.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsOllama_ContextSizing_EveryRequestAsksTheServerForTheStatedWindow()
    {
        // Arrange: a server that answers chat requests, and one size chosen by the application
        const string model = "qwen3:8b";
        const int chosen = 16384;
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/api/chat").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"model":"qwen3:8b","message":{"role":"assistant","content":"ok"},"done":true}"""));
        using var ollama = new OllamaApiClient(new Uri(server.Url!), model);

        // Act: read the window the session will account against, and hold a conversation through
        // the client composed with it
        var window = await OllamaContextWindow.ReadAsync(
            ollama,
            model,
            stated: chosen,
            TestContext.Current.CancellationToken);

        using var client = new OllamaContextSizingChatClient(ollama, window.Tokens);
        await Converse(client);
        await Converse(client);

        // Assert: the window is the stated figure, and every request asked the server for it
        Assert.Equal(chosen, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Stated, window.Source);
        Assert.Equal(2, server.LogEntries.Count);
        Assert.All(server.LogEntries, entry => Assert.Equal(chosen, RequestedContextLength(entry.RequestMessage!.Body)));
    }

    /// <summary>
    ///     Sends one turn of a conversation through a client.
    /// </summary>
    /// <param name="client">The client to send through.</param>
    /// <returns>A task that completes when the turn has been answered.</returns>
    private static Task<ChatResponse> Converse(IChatClient client) =>
        client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options: null,
            TestContext.Current.CancellationToken);

    /// <summary>
    ///     Reads the context length an Ollama chat request asked the server for.
    /// </summary>
    /// <param name="body">The request body the server received.</param>
    /// <returns>The context length the request named.</returns>
    private static int RequestedContextLength(string? body)
    {
        using var request = JsonDocument.Parse(body!);

        return request.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32();
    }
}
