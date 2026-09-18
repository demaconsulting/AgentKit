using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     System-level tests for the AgentKit Ollama Agents package.
/// </summary>
/// <remarks>
///     The package makes one promise an application acts on: hand it what an Ollama server said and
///     it answers with the window a conversation should be accounted against, plus an honest account
///     of where that number came from. This exercises that promise across the whole ladder in one
///     scenario, as a consumer meets it rather than one precedence step at a time.
/// </remarks>
public class AgentKitAgentsOllamaTests
{
    /// <summary>
    ///     Proves the package walks the whole precedence for one model and names the source of every
    ///     figure it reports.
    /// </summary>
    /// <remarks>
    ///     The three states are the ones a real server moves between over a conversation's life: the
    ///     model loaded and enforcing a length, the model known but not loaded, and a server that
    ///     answered nothing. An application reads <c>Tokens</c> to size its session and <c>Source</c>
    ///     to decide how much to trust it, so both are asserted at every step.
    /// </remarks>
    [Fact]
    public void AgentKitAgentsOllama_ContextWindow_ReportsTheEnforcedWindowAndNamesItsSource()
    {
        // Arrange: one model, reported by a server as loaded far below the maximum it publishes
        const string model = "qwen3:8b";
        var loaded = new[]
        {
            new RunningModel { Name = model, ModelName = model, ContextLength = 8192 },
        };
        var published = new ShowModelResponse
        {
            Info = new ModelInfo
            {
                Architecture = "qwen3",
                ExtraInfo = new Dictionary<string, object> { ["qwen3.context_length"] = 262144 },
            },
        };

        // Act: the same model, as the server's state changes over a conversation's life
        var enforced = OllamaContextWindow.Select(stated: null, loaded, published, model);
        var maximum = OllamaContextWindow.Select(stated: null, running: null, published, model);
        var nothing = OllamaContextWindow.Select(stated: null, running: null, published: null, model);

        // Assert: the enforced length while loaded, the published maximum once it is not, and the
        // conservative default when the server said nothing - each named for what it is
        Assert.Multiple(
            () => Assert.Equal(8192, enforced.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.LoadedModel, enforced.Source),
            () => Assert.Equal(262144, maximum.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.PublishedModel, maximum.Source),
            () => Assert.Equal(OllamaContextWindow.AssumedTokens, nothing.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.Assumed, nothing.Source));
    }
}
