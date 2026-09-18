using System.Reflection;
using System.Text.Json;
using DemaConsulting.AgentKit.Agents.Ollama;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Exercises the window precedence against payloads captured from a live Ollama server.
/// </summary>
/// <remarks>
///     <para>
///     The sibling tests build <c>RunningModel</c> and <c>ShowModelResponse</c> by hand, which
///     exercises the precedence but assumes the shape the server sends. That assumption was wrong
///     once already: the published length arrives inside a <c>[JsonExtensionData]</c> dictionary, so
///     it is always a <c>JsonElement</c>, and code written to also accept an <c>int</c> or a string
///     was unreachable. Hand-built objects cannot catch that, because the test author picks the type.
///     </para>
///     <para>
///     These payloads are verbatim server output, so deserialization runs the way it runs in
///     production and the precedence is judged on what Ollama actually said.
///     </para>
/// </remarks>
public sealed class OllamaContextWindowFixtureTests
{
    /// <summary>
    ///     Proves a loaded model's enforced length wins over the model's published maximum.
    /// </summary>
    /// <remarks>
    ///     This is the rung that matters most, and the captured payloads show why: the same model
    ///     publishes a maximum of 262,144 tokens while the server has loaded it at 65,536 — a
    ///     four-fold difference. Accounting against the published figure would have a session rotate
    ///     long after Ollama had already discarded the start of the conversation, losing history
    ///     with nothing reported.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Select_RealPayloads_PrefersTheLoadedLengthOverThePublishedMaximum()
    {
        // Arrange
        var loaded = Read<ListRunningModelsResponse>("ps-one-loaded.json");
        var published = Read<ShowModelResponse>("show-qwen35-9b.json");

        // Act
        var window = OllamaContextWindow.Select(null, loaded.RunningModels, published, "qwen3.5:9b");

        // Assert
        Assert.Equal(65_536, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves the published maximum is used when the server has nothing loaded.
    /// </summary>
    /// <remarks>
    ///     The empty payload is the real one a server returns before any model is resident, which is
    ///     the ordinary state of a box that has just started.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Select_RealPayloads_NothingLoaded_FallsBackToThePublishedMaximum()
    {
        // Arrange
        var none = Read<ListRunningModelsResponse>("ps-none-loaded.json");
        var published = Read<ShowModelResponse>("show-qwen35-9b.json");

        // Act
        var window = OllamaContextWindow.Select(null, none.RunningModels, published, "qwen3.5:9b");

        // Assert
        Assert.Equal(262_144, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.PublishedModel, window.Source);
    }

    /// <summary>
    ///     Proves the published length is read from the architecture-keyed metadata the server sends.
    /// </summary>
    /// <remarks>
    ///     Ollama keys the length by architecture — here <c>qwen35.context_length</c> — inside a
    ///     dictionary the deserializer fills as <c>JsonElement</c> values. Asserting the type is what
    ///     keeps the reader honest: a future change that handles other CLR types would be adding
    ///     branches the server cannot reach.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Select_RealPayloads_PublishedLengthArrivesAsAJsonElement()
    {
        // Arrange
        var published = Read<ShowModelResponse>("show-qwen35-9b.json");

        // Act
        var key = published.Info.ExtraInfo!.Keys.Single(name => name.EndsWith(".context_length", StringComparison.Ordinal));

        // Assert
        Assert.Equal("qwen35.context_length", key);
        Assert.IsType<JsonElement>(published.Info.ExtraInfo[key]);
    }

    /// <summary>
    ///     Reads an embedded fixture captured from a live Ollama server.
    /// </summary>
    /// <typeparam name="T">The response type the payload deserializes to.</typeparam>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The deserialized payload.</returns>
    private static T Read<T>(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(entry => entry.EndsWith(name, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return JsonSerializer.Deserialize<T>(reader.ReadToEnd())!;
    }
}
