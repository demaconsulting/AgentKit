using System.Text;
using System.Text.Json.Nodes;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     An HTTP handler between the OllamaSharp client and the server that pins the request settings
///     a reproducible run needs and records what each chat request carried.
/// </summary>
/// <remarks>
///     <para>
///     Every <c>/api/chat</c> request is rewritten with <c>"think": false</c>, so thinking models
///     answer promptly, and with <c>options.num_ctx</c>, so the context window is the one the run
///     states rather than whatever the server last loaded. Both are recorded in the log.
///     </para>
///     <para>
///     Each request is summarized per message — role, content length, image count, and whether the
///     content carries base64 image text — so a scenario asserts on what the model was actually sent
///     rather than on its prose. Tool results are kept whole for the scenarios that inspect them.
///     </para>
/// </remarks>
/// <param name="numContext">The context length every request asks the server for.</param>
/// <param name="log">Writes one line to the run's log.</param>
internal sealed class WireRecordingHandler(int numContext, Action<string> log) : DelegatingHandler(new HttpClientHandler())
{
    /// <summary>
    ///     Gets the summary of every chat request sent, oldest first.
    /// </summary>
    public List<IReadOnlyList<WireMessage>> Requests { get; } = [];

    /// <summary>
    ///     Gets the number of tools each chat request offered, oldest first.
    /// </summary>
    public List<int> ToolCounts { get; } = [];

    /// <summary>
    ///     Rewrites and records a chat request, then forwards it.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The server's response.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null && request.RequestUri?.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal) == true)
        {
            var node = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!.AsObject();
            node["think"] = false;
            var options = node["options"] as JsonObject ?? [];
            options["num_ctx"] = numContext;
            node["options"] = options;

            var messages = (node["messages"] as JsonArray ?? []).Select(Summarize).ToList();
            Requests.Add(messages);
            ToolCounts.Add((node["tools"] as JsonArray)?.Count ?? 0);
            log($"[wire] POST /api/chat #{Requests.Count} tools={ToolCounts[^1]} num_ctx={numContext} think=false messages={messages.Count}");
            foreach (var message in messages)
            {
                log("[wire]    " + message);
            }

            request.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Summarizes one request message.
    /// </summary>
    /// <param name="message">The message node.</param>
    /// <returns>The summary.</returns>
    private static WireMessage Summarize(JsonNode? message)
    {
        var content = message?["content"]?.GetValue<string>() ?? string.Empty;
        return new WireMessage(
            message?["role"]?.GetValue<string>() ?? "?",
            content,
            (message?["images"] as JsonArray)?.Count ?? 0,
            content.Contains("base64", StringComparison.Ordinal) || content.Contains("iVBORw0KGgo", StringComparison.Ordinal));
    }
}

/// <summary>
///     One message of a recorded chat request.
/// </summary>
/// <param name="Role">The message role.</param>
/// <param name="Content">The message's text content.</param>
/// <param name="Images">The number of images the message carried.</param>
/// <param name="Base64InText">Whether the text content carries base64 image data.</param>
internal sealed record WireMessage(string Role, string Content, int Images, bool Base64InText)
{
    /// <summary>
    ///     Renders the summary for the log, clipping long content.
    /// </summary>
    /// <returns>The rendering.</returns>
    public override string ToString()
    {
        var flat = Content.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        var preview = flat.Length <= 200 ? flat : flat[..200] + "...";
        return $"{Role,-9} chars={Content.Length,7} images={Images} base64InText={Base64InText} | {preview}";
    }
}
