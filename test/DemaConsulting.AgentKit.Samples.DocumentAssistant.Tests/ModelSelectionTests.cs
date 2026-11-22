using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests;

/// <summary>
///     Unit tests for the document-assistant sample's model-selection options and the picker
///     listing they drive.
/// </summary>
/// <remarks>
///     The sample is a vision application: with vision on it lists and selects only models that can
///     see, and it validates the effort, tier and window a user typed before any session starts.
///     These scenarios pin the parsing, the requirement and the listing without contacting a
///     provider.
/// </remarks>
public class ModelSelectionTests
{
    /// <summary>
    ///     Proves the model-setting flags are parsed into the options.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_ModelSettingFlags_AreCarried()
    {
        // Arrange / Act
        var options = CommandLineOptions.Parse(
        [
            "--workspace", "ws", "--provider", "ollama", "--model", "gemma4:e4b", "--reasoning-effort", "none",
            "--context-tier", "default", "--context-window", "8192",
        ]);

        // Assert
        Assert.Multiple(
            () => Assert.Equal("none", options.ReasoningEffort),
            () => Assert.Equal("default", options.ContextTier),
            () => Assert.Equal(8192, options.ContextWindow));
    }

    /// <summary>
    ///     Proves a window that is not a positive whole number is a usage error.
    /// </summary>
    /// <param name="value">The rejected value.</param>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    public void CommandLineOptions_Parse_BadContextWindow_IsAUsageError(string value)
    {
        // Arrange / Act / Assert
        Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--workspace", "ws", "--provider", "ollama", "--context-window", value]));
    }

    /// <summary>
    ///     Proves a context window with the Copilot provider — named, or by default — is a usage
    ///     error, because the Copilot agent the sample builds is accounted against no window and
    ///     would not apply it.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_ContextWindowOnCopilot_IsAUsageError()
    {
        // Arrange / Act
        var named = Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--workspace", "ws", "--provider", "copilot", "--context-window", "8192"]));
        var byDefault = Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--workspace", "ws", "--context-window", "8192"]));

        // Assert: the message says which provider the flag belongs to
        Assert.Contains("--provider ollama only", named.Message, StringComparison.Ordinal);
        Assert.Equal(named.Message, byDefault.Message);
    }

    /// <summary>
    ///     Proves listing models needs no workspace.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_ListModels_NeedsNoWorkspace()
    {
        // Arrange / Act
        var options = CommandLineOptions.Parse(["--provider", "ollama", "--list-models"]);

        // Assert
        Assert.True(options.ListModels);
    }

    /// <summary>
    ///     Proves the sample requires vision only while vision is on.
    /// </summary>
    [Fact]
    public void AgentComposition_RequiredCapabilities_FollowsTheVisionSwitch()
    {
        // Arrange
        var vision = CommandLineOptions.Parse(["--workspace", "ws"]);
        var text = CommandLineOptions.Parse(["--workspace", "ws", "--no-vision"]);

        // Act / Assert
        Assert.Equal(ModelCapabilities.Vision | ModelCapabilities.ToolCalls, AgentComposition.RequiredCapabilities(vision));
        Assert.Equal(ModelCapabilities.ToolCalls, AgentComposition.RequiredCapabilities(text));
    }

    /// <summary>
    ///     Proves the image tools follow the host capabilities a selection derives.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_ImageToolsFollowTheHostCapabilities()
    {
        // Arrange
        var root = Path.GetTempPath();

        // Act
        var seeing = AgentComposition.BuildTools(root, root, false, HostCapabilities.Vision);
        var blind = AgentComposition.BuildTools(root, root, false, HostCapabilities.None);

        // Assert
        Assert.Contains(seeing, tool => tool.Name == "image_read");
        Assert.DoesNotContain(blind, tool => tool.Name.StartsWith("image_", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the listing shows only the models that can run the sample, grouped by category.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentComposition_WriteModelListAsync_ShowsOnlyVisionModelsGroupedByCategory()
    {
        // Arrange: one vision model, one text model
        var catalog = new FixedCatalog(
        [
            new ModelInfo
            {
                Provider = "test", Id = "seeing", DisplayName = "Seeing", Category = "versatile",
                Capabilities = ModelCapabilities.Vision | ModelCapabilities.ToolCalls,
            },
            new ModelInfo
            {
                Provider = "test", Id = "blind", DisplayName = "Blind", Category = "versatile",
                Capabilities = ModelCapabilities.ToolCalls,
            },
        ]);
        var options = CommandLineOptions.Parse(["--list-models"]);
        using var output = new StringWriter();

        // Act
        await AgentComposition.WriteModelListAsync(catalog, options, output, TestContext.Current.CancellationToken);

        // Assert
        var text = output.ToString();
        Assert.Contains("versatile:", text, StringComparison.Ordinal);
        Assert.Contains("seeing (Seeing)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("blind", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A catalog that always lists the same models.
    /// </summary>
    /// <param name="models">The models.</param>
    private sealed class FixedCatalog(IReadOnlyList<ModelInfo> models) : CachingModelCatalog("test")
    {
        /// <inheritdoc/>
        protected override Task<IReadOnlyList<ModelInfo>> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(models);
    }
}
