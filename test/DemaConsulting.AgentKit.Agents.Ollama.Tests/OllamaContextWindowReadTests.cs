using System.Reflection;
using OllamaSharp;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Exercises the reading of an Ollama server through the real client over HTTP.
/// </summary>
/// <remarks>
///     <para>
///     <c>ReadAsync</c> is the method an application actually calls, and the only honest way to test
///     it is to run the real <c>OllamaApiClient</c>. Hand-faking <c>IOllamaApiClient</c> would assert
///     this project's beliefs about OllamaSharp back at it rather than test them, and that has
///     already cost one shipped defect here — a message shape the fakes accepted and the real wire
///     discarded. This is why the repository takes a mocking library at all, and why it is an HTTP
///     one: WireMock.Net stands up a server on loopback so the client's own request shaping and its
///     own deserialization execute.
///     </para>
///     <para>
///     The payloads are the three the precedence tests use, captured verbatim from a live Ollama
///     0.34.1 server, replayed from the endpoints the client really calls — <c>GET /api/ps</c> and
///     <c>POST /api/show</c>. The cases the precedence tests cannot reach are the failure ones:
///     <c>Select</c> is only ever handed what survived, so nothing beneath it can show that a server
///     declining a query costs a rung of the ladder rather than the run. That tolerance is the
///     reason this file exists.
///     </para>
/// </remarks>
public sealed class OllamaContextWindowReadTests
{
    /// <summary>
    ///     The model these tests ask about, as the captured payloads name it.
    /// </summary>
    private const string Model = "qwen3.5:9b";

    /// <summary>
    ///     The endpoint the client asks for the models a server currently holds loaded.
    /// </summary>
    private const string RunningModelsPath = "/api/ps";

    /// <summary>
    ///     The endpoint the client asks for a model's published metadata.
    /// </summary>
    private const string ModelMetadataPath = "/api/show";

    /// <summary>
    ///     Proves the length the server loaded the model with is what gets reported, read back out
    ///     of what an Ollama server actually sent.
    /// </summary>
    /// <remarks>
    ///     The two figures arrive in the same exchange and differ four-fold — 65,536 enforced
    ///     against a published maximum of 262,144 — so this is the decision the whole discovery
    ///     exists to make, proven over the wire rather than over values a test author shaped.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces()
    {
        // Arrange: a server holding the model loaded, and publishing a far larger maximum for it
        using var server = WireMockServer.Start();
        Serve(server, "GET", RunningModelsPath, "ps-one-loaded.json");
        Serve(server, "POST", ModelMetadataPath, "show-qwen35-9b.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act: read the window with nothing stated, so the server settles it
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the enforced length, named as the loaded model's
        Assert.Equal(65_536, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves the published maximum is reported when the server holds nothing loaded.
    /// </summary>
    /// <remarks>
    ///     The empty loaded-model report is the one a server really returns before anything is
    ///     resident, which is the ordinary state of a box that has just started. The query answered;
    ///     it simply had no model to name, which is a different rung from a query that failed.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_NothingLoaded_ReportsThePublishedMaximum()
    {
        // Arrange: a server with nothing resident, still publishing the model's metadata
        using var server = WireMockServer.Start();
        Serve(server, "GET", RunningModelsPath, "ps-none-loaded.json");
        Serve(server, "POST", ModelMetadataPath, "show-qwen35-9b.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the maximum, named as published rather than as the limit in force
        Assert.Equal(262_144, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.PublishedModel, window.Source);
    }

    /// <summary>
    ///     Proves a stated window settles the question without the server being asked at all.
    /// </summary>
    /// <remarks>
    ///     Both endpoints are mapped, so a query would have succeeded and would have produced a
    ///     different figure. That the server logged nothing is the assertion: the stated window is
    ///     not merely preferred over an answer, it is preferred over asking.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_StatedWindow_DoesNotQueryTheServer()
    {
        // Arrange: a server that would answer with 65,536 if it were asked
        using var server = WireMockServer.Start();
        Serve(server, "GET", RunningModelsPath, "ps-one-loaded.json");
        Serve(server, "POST", ModelMetadataPath, "show-qwen35-9b.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act: the application states its own window
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: 2048, TestContext.Current.CancellationToken);

        // Assert: the stated figure, and a server that received nothing
        Assert.Equal(2048, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Stated, window.Source);
        Assert.Empty(server.LogEntries);
    }

    /// <summary>
    ///     Proves a declined loaded-model query costs a rung of the ladder rather than the run.
    /// </summary>
    /// <remarks>
    ///     A server declines this query for ordinary reasons — an older build, a proxy in front of
    ///     it — and the reading is called unconditionally wherever a provider is configured, so a
    ///     failure here must yield the next source down rather than an exception an application
    ///     would have to start around.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_RunningModelsQueryFails_ReportsThePublishedMaximum()
    {
        // Arrange: the loaded-model query fails while the metadata query answers
        using var server = WireMockServer.Start();
        Decline(server, "GET", RunningModelsPath);
        Serve(server, "POST", ModelMetadataPath, "show-qwen35-9b.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the figure the server did offer, named for what it is
        Assert.Equal(262_144, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.PublishedModel, window.Source);
    }

    /// <summary>
    ///     Proves a declined metadata query leaves the enforced length still reported.
    /// </summary>
    /// <remarks>
    ///     The mirror of the previous case, and the more valuable direction: the figure that
    ///     survives here is the one the server will actually enforce, so a metadata query that a
    ///     model never pulled would fail must not cost the reading its best answer.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_ModelMetadataQueryFails_ReportsTheEnforcedLength()
    {
        // Arrange: the metadata query fails while the loaded-model query answers
        using var server = WireMockServer.Start();
        Serve(server, "GET", RunningModelsPath, "ps-one-loaded.json");
        Decline(server, "POST", ModelMetadataPath);
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(65_536, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves a server that answers neither query yields the conservative default, named as
    ///     assumed.
    /// </summary>
    /// <remarks>
    ///     The bottom of the ladder reached through the transport rather than through the
    ///     precedence: an application talking to a server that declines everything still starts,
    ///     and is told plainly that the figure it got was assumed rather than measured.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_NeitherQueryAnswers_AssumesTheOllamaDefault()
    {
        // Arrange: both endpoints decline
        using var server = WireMockServer.Start();
        Decline(server, "GET", RunningModelsPath);
        Decline(server, "POST", ModelMetadataPath);
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: Ollama's own default, named as assumed
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves cancellation stops the reading rather than being swallowed into an assumed window.
    /// </summary>
    /// <remarks>
    ///     The failure tolerance that makes the other cases work is exactly what could hide a
    ///     cancellation, and a canceled call that quietly returned a window would have the caller
    ///     continue on a figure it never asked for. The token is canceled before the call, which the
    ///     client checks before sending, so the test is deterministic rather than timing-dependent.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_CanceledToken_PropagatesTheCancellation()
    {
        // Arrange: a server that would answer, and a token already canceled
        using var server = WireMockServer.Start();
        Serve(server, "GET", RunningModelsPath, "ps-one-loaded.json");
        Serve(server, "POST", ModelMetadataPath, "show-qwen35-9b.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert: the cancellation surfaces, and the server was never asked
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OllamaContextWindow.ReadAsync(client, Model, stated: null, canceled.Token));
        Assert.Empty(server.LogEntries);
    }

    /// <summary>
    ///     Answers an endpoint with a captured payload, as a working server would.
    /// </summary>
    /// <param name="server">The mock server to configure.</param>
    /// <param name="method">The HTTP method the Ollama client uses for this endpoint.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="fixture">The captured fixture file name to serve.</param>
    private static void Serve(WireMockServer server, string method, string path, string fixture) =>
        server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture(fixture)));

    /// <summary>
    ///     Declines an endpoint with a server error, as an older build or a proxy would.
    /// </summary>
    /// <remarks>
    ///     Mapped explicitly rather than left unmapped so the refusal is visible in the arrangement
    ///     instead of resting on what the mock server does with a route it was never told about.
    /// </remarks>
    /// <param name="server">The mock server to configure.</param>
    /// <param name="method">The HTTP method the Ollama client uses for this endpoint.</param>
    /// <param name="path">The endpoint path.</param>
    private static void Decline(WireMockServer server, string method, string path) =>
        server
            .Given(Request.Create().WithPath(path).UsingMethod(method))
            .RespondWith(Response.Create().WithStatusCode(500));

    /// <summary>
    ///     Reads an embedded fixture captured from a live Ollama server, verbatim.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The payload text, exactly as the server sent it.</returns>
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(entry => entry.EndsWith(name, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
