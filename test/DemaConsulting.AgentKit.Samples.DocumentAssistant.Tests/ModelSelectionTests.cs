using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests;

/// <summary>
///     Unit tests for the document-assistant sample's model-selection options and the picker
///     listing they drive.
/// </summary>
/// <remarks>
///     The sample is a vision application: with vision on it lists and selects only models that can
///     see, and it validates the effort, tier and window a user typed before any session starts.
///     These scenarios pin the parsing, the requirement, the listing and the tools each choice
///     composes — the image and diagram tools following the host — without contacting a
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
    ///     Proves the diagram tools are offered by default and <c>--no-diagrams</c> turns them off,
    ///     independently of <c>--no-vision</c>.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_NoDiagrams_DisablesOnlyTheDiagrams()
    {
        // Arrange / Act
        var defaults = CommandLineOptions.Parse(["--workspace", "ws"]);
        var noDiagrams = CommandLineOptions.Parse(["--workspace", "ws", "--no-diagrams"]);
        var noVision = CommandLineOptions.Parse(["--workspace", "ws", "--no-vision"]);

        // Assert: each switch removes only what it names
        Assert.Multiple(
            () => Assert.True(defaults.DiagramsEnabled),
            () => Assert.False(noDiagrams.DiagramsEnabled),
            () => Assert.True(noDiagrams.VisionEnabled),
            () => Assert.True(noVision.DiagramsEnabled));
    }

    /// <summary>
    ///     Proves the diagram pack's preview follows the host and the switch removes it.
    /// </summary>
    [Fact]
    public void AgentComposition_CreateDiagramPack_FollowsTheHostAndTheSwitch()
    {
        // Arrange / Act
        var seeing = AgentComposition.CreateDiagramPack(HostCapabilities.Vision, true);
        var blind = AgentComposition.CreateDiagramPack(HostCapabilities.None, true);
        var off = AgentComposition.CreateDiagramPack(HostCapabilities.Vision, false);

        // Assert: a host that can see is shown each drawing, one that cannot is told about it
        Assert.Multiple(
            () => Assert.Equal(DiagramPreview.Image, seeing?.Preview),
            () => Assert.Equal(DiagramPreview.Text, blind?.Preview),
            () => Assert.Null(off));
    }

    /// <summary>
    ///     Proves a vision host is offered the diagram tools alongside <c>image_view</c>.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_VisionHostWithDiagrams_OffersTheDiagramAndViewTools()
    {
        // Arrange
        var root = Path.GetTempPath();
        var diagrams = AgentComposition.CreateDiagramPack(HostCapabilities.Vision, true);

        // Act
        var tools = AgentComposition.BuildTools(root, root, false, HostCapabilities.Vision, diagrams);

        // Assert
        Assert.Multiple(
            () => Assert.Contains(tools, tool => tool.Name == "diagram_help"),
            () => Assert.Contains(tools, tool => tool.Name == "diagram_render"),
            () => Assert.Contains(tools, tool => tool.Name == "image_view"));
    }

    /// <summary>
    ///     Proves a host that cannot see keeps the diagram tools, in text preview, without any image
    ///     tool.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_BlindHostWithDiagrams_OffersTheDiagramToolsOnly()
    {
        // Arrange
        var root = Path.GetTempPath();
        var diagrams = AgentComposition.CreateDiagramPack(HostCapabilities.None, true);

        // Act
        var tools = AgentComposition.BuildTools(root, root, false, HostCapabilities.None, diagrams);

        // Assert: the session folder is writable, so text preview's render tool is published
        Assert.Multiple(
            () => Assert.Contains(tools, tool => tool.Name == "diagram_help"),
            () => Assert.Contains(tools, tool => tool.Name == "diagram_render"),
            () => Assert.DoesNotContain(tools, tool => tool.Name.StartsWith("image_", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Proves a run without a diagram pack offers no diagram tool.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_NoDiagramPack_OffersNoDiagramTool()
    {
        // Arrange
        var root = Path.GetTempPath();
        var diagrams = AgentComposition.CreateDiagramPack(HostCapabilities.Vision, false);

        // Act
        var tools = AgentComposition.BuildTools(root, root, false, HostCapabilities.Vision, diagrams);

        // Assert
        Assert.DoesNotContain(tools, tool => tool.Name.StartsWith("diagram_", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the composed <c>diagram_render</c> saves a drawing of the release process into the
    ///     session folder, under a read-only workspace, which is the sample's diagram scenario.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentComposition_BuildTools_DiagramRender_SavesThePictureIntoTheSessionFolder()
    {
        // Arrange: a read-only workspace and a separate writable session folder, as the sample grants
        var root = Path.Combine(Path.GetTempPath(), "document-assistant-tests-" + Guid.NewGuid().ToString("N"));
        var workspace = Directory.CreateDirectory(Path.Combine(root, "workspace")).FullName;
        var session = Directory.CreateDirectory(Path.Combine(root, "session")).FullName;
        try
        {
            var diagrams = AgentComposition.CreateDiagramPack(HostCapabilities.Vision, true);
            var tools = AgentComposition.BuildTools(workspace, session, true, HostCapabilities.Vision, diagrams);
            var render = tools.Single(tool => tool.Name == "diagram_render");
            var target = Path.Combine(session, "release-process.png");

            // Act: draw a flowchart and save it under the session folder's absolute path
            var result = await render.InvokeAsync(
                new AIFunctionArguments
                {
                    ["format"] = "mermaid",
                    ["source"] = "flowchart TD\n  A[Create release branch] --> B{Tests pass?}\n  B -- yes --> C[Tag and publish]\n  B -- no --> A\n",
                    ["save_as"] = target,
                },
                TestContext.Current.CancellationToken);

            // Assert: the picture came back to look at and the saved file is a PNG
            Assert.IsType<List<AIContent>>(result);
            var saved = await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken);
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], saved[..4]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
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
