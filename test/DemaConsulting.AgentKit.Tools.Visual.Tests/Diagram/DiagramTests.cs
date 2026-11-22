using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Diagram;

/// <summary>
///     Subsystem tests for the Diagram subsystem: the family composed through a builder and used
///     the way a model uses it.
/// </summary>
public class DiagramTests
{
    /// <summary>
    ///     Proves the family composed through a builder on a vision host publishes both tools under
    ///     the family prefix, in the family's order.
    /// </summary>
    [Fact]
    public void Diagram_Family_ComposedThroughBuilder_PublishesBothTools()
    {
        // Arrange: a vision host with the family attached
        var builder = new ToolPackBuilder(new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(DiagramPack.For(HostCapabilities.Vision));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: help then render, both under the prefix
        Assert.Equal([DiagramHelpTool.ToolName, DiagramRenderTool.ToolName], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a host that cannot see receives the family in text preview, and an image-preview
    ///     pack on such a host contributes nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Diagram_Family_HostWithoutVision_ReceivesTheTextPreview()
    {
        // Arrange: a host declaring nothing, over a writable workspace
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var textTools = new ToolPackBuilder(policy).Add(DiagramPack.For(HostCapabilities.None)).Build();
        var imageTools = new ToolPackBuilder(policy).Add(new DiagramPack(DiagramPreview.Image)).Build();

        // Act: draw through the text-preview family
        var result = await textTools.Single(tool => tool.Name == DiagramRenderTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid, ["save_as"] = "flow.png" },
            TestContext.Current.CancellationToken);

        // Assert: the text preview is offered and describes the picture; image preview is not offered
        Assert.Equal([DiagramHelpTool.ToolName, DiagramRenderTool.ToolName], textTools.Select(tool => tool.Name));
        Assert.Contains("Draft", result!.ToString(), StringComparison.Ordinal);
        Assert.Empty(imageTools);
    }

    /// <summary>
    ///     Proves the draw, read the help, fix and draw again loop: a broken source names a help
    ///     topic, the help tool serves that topic, and the fixed source is drawn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Diagram_DrawFixRedraw_BrokenSource_NamesAServedTopicAndTheFixDraws()
    {
        // Arrange: the family on a vision host
        var tools = new ToolPackBuilder(new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new DiagramPack())
            .Build();
        var help = tools.Single(tool => tool.Name == DiagramHelpTool.ToolName);
        var render = tools.Single(tool => tool.Name == DiagramRenderTool.ToolName);

        // Act: draw a broken source, read the topic it names, then draw the fixed source
        var failure = Assert.IsType<string>(await render.InvokeAsync(
            new AIFunctionArguments { ["format"] = "mermaid", ["source"] = RenderSamples.MermaidBadArrow },
            TestContext.Current.CancellationToken));
        var topic = failure.Split('\'')[^2];
        var card = Assert.IsType<string>(await help.InvokeAsync(
            new AIFunctionArguments { ["topic"] = topic },
            TestContext.Current.CancellationToken));
        var fixedResult = await render.InvokeAsync(
            new AIFunctionArguments { ["format"] = "mermaid", ["source"] = RenderSamples.MermaidBadArrow.Replace("->", "-->", StringComparison.Ordinal) },
            TestContext.Current.CancellationToken);

        // Assert: the failure located the line, its topic was served, and the fix was drawn
        Assert.StartsWith("Render failed (mermaid-error", failure, StringComparison.Ordinal);
        Assert.Equal("mermaid/diagnostics", topic);
        Assert.Contains("mermaid-syntax-error", card, StringComparison.Ordinal);
        Assert.IsType<List<AIContent>>(fixedResult);
    }

    /// <summary>
    ///     Proves the help is progressive: the index is small and names the topics, and a topic's card
    ///     names the topics below it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Diagram_ProgressiveHelp_IndexThenTopic_NarrowsToTheDiagramType()
    {
        // Arrange: the help tool of a default pack
        var help = new DiagramPack()
            .CreateTools(new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]))
            .Single(tool => tool.Name == DiagramHelpTool.ToolName);

        // Act: read the index, then a language, then a diagram type
        var index = Assert.IsType<string>(await help.InvokeAsync([], TestContext.Current.CancellationToken));
        var language = Assert.IsType<string>(await help.InvokeAsync(new AIFunctionArguments { ["topic"] = "plantuml" }, TestContext.Current.CancellationToken));
        var type = Assert.IsType<string>(await help.InvokeAsync(new AIFunctionArguments { ["topic"] = "plantuml/sequence" }, TestContext.Current.CancellationToken));

        // Assert: each step names the next, and the index stays small
        Assert.Contains("plantuml ", index, StringComparison.Ordinal);
        Assert.Contains("- plantuml/sequence", language, StringComparison.Ordinal);
        Assert.Contains("@startuml", type, StringComparison.Ordinal);
        Assert.True(index.Length < language.Length + type.Length);
    }
}
