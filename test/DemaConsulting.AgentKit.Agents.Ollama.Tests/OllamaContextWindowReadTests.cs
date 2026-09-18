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
///     The payloads are captured verbatim from a live Ollama 0.34.1 server and replayed from the one
///     endpoint the client really calls, <c>GET /api/ps</c>. The cases the precedence tests cannot
///     reach are the failure ones: <c>Select</c> is only ever handed what survived, so nothing
///     beneath it can show that a server declining the query costs a rung of the ladder rather than
///     the run. That tolerance is the reason this file exists.
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
    ///     Proves the length the server loaded the model with is what gets reported, read back out
    ///     of what an Ollama server actually sent.
    /// </summary>
    /// <remarks>
    ///     This is the only figure that describes the instance rather than the file, and it is the
    ///     decision the whole discovery exists to make, proven over the wire rather than over values
    ///     a test author shaped.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces()
    {
        // Arrange: a server holding the model loaded
        using var server = WireMockServer.Start();
        Serve(server, RunningModelsPath, "ps-one-loaded.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act: read the window with nothing stated, so the server settles it
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the enforced length, named as the loaded model's
        Assert.Equal(65_536, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves the conservative default is reported when the server holds nothing loaded.
    /// </summary>
    /// <remarks>
    ///     The empty loaded-model report is the one a server really returns before anything is
    ///     resident, which is the ordinary state of a box that has just started. The query answered;
    ///     it simply had no instance to describe, and nothing about the model file says what an
    ///     instance would be loaded at.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_NothingLoaded_AssumesTheOllamaDefault()
    {
        // Arrange: a server with nothing resident
        using var server = WireMockServer.Start();
        Serve(server, RunningModelsPath, "ps-none-loaded.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: Ollama's own default, named as assumed rather than measured
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves the model's published maximum is never asked for, whatever the server would say.
    /// </summary>
    /// <remarks>
    ///     A published maximum describes what the model file could support, not what the instance is
    ///     running: one live server published 262,144 for a model it was running at 4,096. Accounting
    ///     against the larger figure would keep a session talking long after the server had discarded
    ///     the start of the conversation. Asserting that the endpoint is never called pins the rung's
    ///     removal rather than merely the fact that nothing currently reaches it.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_ModelNotLoaded_NeverAsksForThePublishedMaximum()
    {
        // Arrange: a server with nothing resident that would happily answer a metadata query
        using var server = WireMockServer.Start();
        Serve(server, RunningModelsPath, "ps-none-loaded.json");
        server
            .Given(Request.Create().WithPath("/api/show").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the assumed default, and a metadata endpoint that was never asked
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
        Assert.DoesNotContain(
            server.LogEntries,
            entry => entry.RequestMessage!.Path.Contains("/api/show", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a stated window settles the question without the server being asked at all.
    /// </summary>
    /// <remarks>
    ///     The endpoint is mapped, so a query would have succeeded and would have produced a
    ///     different figure. That the server logged nothing is the assertion: the stated window is
    ///     not merely preferred over an answer, it is preferred over asking — which is also what
    ///     keeps discovery from loading a model as a side effect.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_StatedWindow_DoesNotQueryTheServer()
    {
        // Arrange: a server that would answer with 65,536 if it were asked
        using var server = WireMockServer.Start();
        Serve(server, RunningModelsPath, "ps-one-loaded.json");
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
    ///     failure here must yield the conservative default rather than an exception an application
    ///     would have to start around.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_RunningModelsQueryFails_AssumesTheOllamaDefault()
    {
        // Arrange: the loaded-model query fails
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath(RunningModelsPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);

        // Act
        var window = await OllamaContextWindow.ReadAsync(client, Model, stated: null, TestContext.Current.CancellationToken);

        // Assert: the conservative default, named as assumed
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
        Serve(server, RunningModelsPath, "ps-one-loaded.json");
        using var client = new OllamaApiClient(new Uri(server.Url!), Model);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert: the cancellation surfaces, and the server was never asked
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OllamaContextWindow.ReadAsync(client, Model, stated: null, canceled.Token));
        Assert.Empty(server.LogEntries);
    }

    /// <summary>
    ///     Proves an unresponsive server costs a rung rather than failing the run.
    /// </summary>
    /// <remarks>
    ///     A server that accepts the connection and then never answers is one of the cases this
    ///     discovery promises to tolerate, but it is reported as <c>TaskCanceledException</c> —
    ///     which derives from <see cref="OperationCanceledException"/> and so is indistinguishable
    ///     by type from a caller who cancelled. Only the token tells them apart. A proxy returning
    ///     504 already degraded correctly; a proxy that black-holes the request did not.
    /// </remarks>
    [Fact]
    public async Task OllamaContextWindow_ReadAsync_ServerNeverAnswers_AssumesTheOllamaDefault()
    {
        // Arrange: the one endpoint hangs far past the client's timeout. The margin is one-sided
        // now that there is a single query: nothing else has to be served inside the timeout, so
        // the only requirement is that the delay dwarfs it and the hang is never a race.
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath(RunningModelsPath).UsingGet())
            .RespondWith(Response.Create().WithDelay(TimeSpan.FromSeconds(60)).WithStatusCode(200));

        using var http = new HttpClient { BaseAddress = new Uri(server.Url!), Timeout = TimeSpan.FromSeconds(4) };
        using var client = new OllamaApiClient(http, Model);

        // Act: the caller's token is never cancelled - only the client's own timeout fires
        var window = await OllamaContextWindow.ReadAsync(
            client,
            Model,
            stated: null,
            TestContext.Current.CancellationToken);

        // Assert: discovery fell to the conservative default instead of throwing
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Answers an endpoint with a captured payload, as a working server would.
    /// </summary>
    /// <param name="server">The mock server to configure.</param>
    /// <param name="path">The endpoint path.</param>
    /// <param name="fixture">The captured fixture file name to serve.</param>
    private static void Serve(WireMockServer server, string path, string fixture) =>
        server
            .Given(Request.Create().WithPath(path).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture(fixture)));

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
