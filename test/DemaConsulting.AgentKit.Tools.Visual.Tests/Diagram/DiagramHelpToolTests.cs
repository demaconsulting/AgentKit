using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Diagram;

/// <summary>
///     Unit tests for the <see cref="DiagramHelpTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would, over the rendering
///     library's real help catalog filtered as a pack filters it, because what the tool promises is
///     the text a model reads.
/// </remarks>
public class DiagramHelpToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void DiagramHelpTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = DiagramHelpTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("diagram_help", name);
        Assert.StartsWith(DiagramPack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a description naming the
    ///     offered languages, and that a missing catalog or format list is a programming error.
    /// </summary>
    [Fact]
    public void DiagramHelpTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a catalog for two languages
        string[] formats = ["mermaid", "svg"];
        var catalog = DiagramHelpTool.CatalogFor(formats);

        // Act: construct the tool
        var tool = DiagramHelpTool.Create(catalog, formats);

        // Assert: the model sees the name and a description naming the languages
        Assert.Equal(DiagramHelpTool.ToolName, tool.Name);
        Assert.Contains("mermaid, svg", tool.Description, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => DiagramHelpTool.Create(null!, formats));
        Assert.Throws<ArgumentNullException>(() => DiagramHelpTool.Create(catalog, null!));
    }

    /// <summary>
    ///     Proves the index lists the top-level topics of the offered languages with their
    ///     summaries, and none of the cards written for the application.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramHelpTool_Help_NoTopic_ReturnsTheIndexOfTopLevelTopics()
    {
        // Arrange: the tool for every language
        var tool = CreateTool(DiagramPackOptions.Default.Formats);

        // Act: ask with no topic
        var index = Assert.IsType<string>(await tool.InvokeAsync([], TestContext.Current.CancellationToken));

        // Assert: one line per top-level topic, no host-oriented card, and a pointer to topics
        var lines = index.Split('\n');
        Assert.Contains(lines, line => line.StartsWith("mermaid ", StringComparison.Ordinal) && line.Contains("Mermaid diagrams", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("scad ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("mermaid/flowchart", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("files ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("limits ", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("pdf ", StringComparison.Ordinal));
        Assert.Contains("with a topic id", index, StringComparison.Ordinal);
        Assert.True(index.Length < 4_000, $"The index was {index.Length} characters.");
    }

    /// <summary>
    ///     Proves a topic returns its card, with the full text of each example source, and an
    ///     alias finds the same card.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramHelpTool_Help_Topic_ReturnsTheCardWithItsExampleSources()
    {
        // Arrange: the tool for every language
        var tool = CreateTool(DiagramPackOptions.Default.Formats);

        // Act: ask for a card by its id, and by an alias
        var card = Assert.IsType<string>(await InvokeAsync(tool, "mermaid/flowchart"));
        var alias = Assert.IsType<string>(await InvokeAsync(tool, " Mermaid/Graph "));

        // Assert: the card's text, its example in a fenced block, and the same card by alias
        Assert.StartsWith("# Flowcharts", card, StringComparison.Ordinal);
        Assert.Contains("## Example sources", card, StringComparison.Ordinal);
        Assert.Contains("```mermaid\n", card, StringComparison.Ordinal);
        Assert.Contains("flowchart LR", card, StringComparison.Ordinal);
        Assert.Equal(card, alias);
    }

    /// <summary>
    ///     Proves a card lists the topics below it, and the cards written for the application are
    ///     hidden from that list and cannot be found.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramHelpTool_Help_HostOrientedCards_AreHidden()
    {
        // Arrange: the tool for every language
        var tool = CreateTool(DiagramPackOptions.Default.Formats);

        // Act: read a language's card, and ask for host-oriented cards
        var mermaid = Assert.IsType<string>(await InvokeAsync(tool, "mermaid"));
        var options = Assert.IsType<string>(await InvokeAsync(tool, "mermaid/options"));
        var files = Assert.IsType<string>(await InvokeAsync(tool, "files"));

        // Assert: the topics below are listed, without the options card; host cards are refused
        Assert.Contains("- mermaid/flowchart", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("- mermaid/options", mermaid, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", options, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", files, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unknown topic is refused with suggestions of served topics, or with a pointer
    ///     to the index when nothing resembles it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramHelpTool_Help_UnknownTopic_IsRefusedWithSuggestions()
    {
        // Arrange: the tool for every language
        var tool = CreateTool(DiagramPackOptions.Default.Formats);

        // Act: ask for a misplaced topic, and a topic nothing resembles
        var near = Assert.IsType<string>(await InvokeAsync(tool, "flowchart"));
        var far = Assert.IsType<string>(await InvokeAsync(tool, "zzzz"));

        // Assert: the first suggests the served card; the second points to the index
        Assert.StartsWith("Denied (TargetNotFound)", near, StringComparison.Ordinal);
        Assert.Contains("Did you mean: mermaid/flowchart", near, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", far, StringComparison.Ordinal);
        Assert.Contains("with no topic for the index", far, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the help of a language the pack does not offer is not served.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramHelpTool_Help_UnofferedLanguage_IsNotServed()
    {
        // Arrange: the tool for Mermaid alone
        var tool = CreateTool(["mermaid"]);

        // Act: read the index, and ask for another language's card
        var index = Assert.IsType<string>(await tool.InvokeAsync([], TestContext.Current.CancellationToken));
        var plantuml = Assert.IsType<string>(await InvokeAsync(tool, "plantuml"));

        // Assert: the other language is absent from the index and refused by id
        Assert.Contains("Diagram help topics for mermaid:", index, StringComparison.Ordinal);
        Assert.DoesNotContain("plantuml", index, StringComparison.Ordinal);
        Assert.DoesNotContain("\nscad ", index, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", plantuml, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the tool as a pack offering the given languages builds it.
    /// </summary>
    /// <param name="formats">The offered languages.</param>
    /// <returns>The tool.</returns>
    private static AIFunction CreateTool(IReadOnlyList<string> formats)
    {
        return DiagramHelpTool.Create(DiagramHelpTool.CatalogFor(formats), formats);
    }

    /// <summary>
    ///     Invokes the tool for one topic exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="topic">The topic.</param>
    /// <returns>The result.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, string topic)
    {
        return await tool.InvokeAsync(new AIFunctionArguments { ["topic"] = topic }, TestContext.Current.CancellationToken);
    }
}
