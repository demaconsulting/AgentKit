using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Diagram;

/// <summary>
///     Unit tests for the <see cref="DiagramPack"/> class.
/// </summary>
public class DiagramPackTests
{
    /// <summary>
    ///     Proves the published family prefix is <c>diagram</c>.
    /// </summary>
    [Fact]
    public void DiagramPack_FamilyPrefix_Constant_IsDiagram()
    {
        // Arrange / Act: read the published constant
        var prefix = DiagramPack.FamilyPrefix;

        // Assert: the family's identity
        Assert.Equal("diagram", prefix);
    }

    /// <summary>
    ///     Proves the pack reports the declared constant through the contract.
    /// </summary>
    [Fact]
    public void DiagramPack_FamilyPrefix_Contract_ReportsTheDeclaredConstant()
    {
        // Arrange: the pack seen through the contract
        IToolPack pack = new DiagramPack();

        // Act: read the prefix the composition reads
        var prefix = pack.FamilyPrefix;

        // Assert: the constant and the contract agree
        Assert.Equal(DiagramPack.FamilyPrefix, prefix);
    }

    /// <summary>
    ///     Proves image preview requires vision and text preview requires nothing.
    /// </summary>
    [Fact]
    public void DiagramPack_RequiredCapabilities_FollowThePreviewMode()
    {
        // Arrange / Act: one pack in each mode, and the default
        var image = new DiagramPack(DiagramPreview.Image);
        var text = new DiagramPack(DiagramPreview.Text);
        var defaults = new DiagramPack();

        // Assert: only a pack returning pictures needs a host that can see
        Assert.Equal(HostCapabilities.Vision, image.RequiredCapabilities);
        Assert.Equal(HostCapabilities.None, text.RequiredCapabilities);
        Assert.Equal(DiagramPreview.Image, defaults.Preview);
        Assert.Equal(HostCapabilities.Vision, defaults.RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the preview mode is chosen from the host's capabilities.
    /// </summary>
    [Fact]
    public void DiagramPack_For_HostCapabilities_ChoosesThePreviewMode()
    {
        // Arrange / Act: a pack for a host that can see, one for a host that cannot, and one for a
        // host declaring vision among other capabilities
        var seeing = DiagramPack.For(HostCapabilities.Vision);
        var blind = DiagramPack.For(HostCapabilities.None);
        var both = DiagramPack.For(HostCapabilities.Vision | HostCapabilities.Delegation);

        // Assert: pictures for a seeing host, descriptions otherwise
        Assert.Equal(DiagramPreview.Image, seeing.Preview);
        Assert.Equal(DiagramPreview.Text, blind.Preview);
        Assert.Equal(DiagramPreview.Image, both.Preview);
    }

    /// <summary>
    ///     Proves the options given are the options applied, the defaults apply when none are given,
    ///     and <see cref="DiagramPack.For"/> passes its options through.
    /// </summary>
    [Fact]
    public void DiagramPack_Constructor_Options_AreApplied()
    {
        // Arrange: options other than the defaults
        var options = new DiagramPackOptions { Formats = ["plantuml"] };

        // Act: construct with them, without them, and through For
        var given = new DiagramPack(DiagramPreview.Image, options);
        var defaults = new DiagramPack();
        var forHost = DiagramPack.For(HostCapabilities.None, options);

        // Assert: the options are carried as given
        Assert.Same(options, given.Options);
        Assert.Same(DiagramPackOptions.Default, defaults.Options);
        Assert.Same(options, forHost.Options);
    }

    /// <summary>
    ///     Proves an undefined preview mode and a smallest preview side above the largest render
    ///     side are refused at construction.
    /// </summary>
    [Fact]
    public void DiagramPack_Constructor_InvalidArguments_AreRefused()
    {
        // Arrange: bounds whose two sides contradict each other
        var options = new DiagramPackOptions
        {
            Visual = new VisualOptions { MaxRenderSide = 500, MinPreviewSide = 600 }
        };

        // Act / Assert: each mistake is refused where it was made
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagramPack((DiagramPreview)7));
        Assert.Throws<ArgumentException>(() => new DiagramPack(DiagramPreview.Image, options));
        Assert.Throws<ArgumentException>(() => DiagramPack.For(HostCapabilities.Vision, options));
    }

    /// <summary>
    ///     Proves the pack creates the help tool and the render tool, with no null element, each
    ///     carrying the family prefix.
    /// </summary>
    [Fact]
    public void DiagramPack_CreateTools_Policy_CreatesTheHelpAndRenderTools()
    {
        // Arrange: a read-write policy
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the tools in each mode
        var image = new DiagramPack(DiagramPreview.Image).CreateTools(policy).ToList();
        var text = new DiagramPack(DiagramPreview.Text).CreateTools(policy).ToList();

        // Assert: both tools, in order, each under the family prefix
        Assert.Equal([DiagramHelpTool.ToolName, DiagramRenderTool.ToolName], image.Select(tool => tool.Name));
        Assert.Equal([DiagramHelpTool.ToolName, DiagramRenderTool.ToolName], text.Select(tool => tool.Name));
        Assert.All(image.Concat(text), tool =>
        {
            Assert.NotNull(tool);
            Assert.StartsWith(DiagramPack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Proves text preview withholds the render tool under a policy that permits no writing,
    ///     while image preview keeps it.
    /// </summary>
    [Fact]
    public void DiagramPack_CreateTools_ReadOnlyPolicy_WithholdsTheTextPreviewRenderTool()
    {
        // Arrange: a policy whose every grant is read-only
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: create the tools in each mode
        var image = new DiagramPack(DiagramPreview.Image).CreateTools(policy).Select(tool => tool.Name).ToList();
        var text = new DiagramPack(DiagramPreview.Text).CreateTools(policy).Select(tool => tool.Name).ToList();

        // Assert: text preview's only outcome would be a refusal, so it is withheld
        Assert.Equal([DiagramHelpTool.ToolName, DiagramRenderTool.ToolName], image);
        Assert.Equal([DiagramHelpTool.ToolName], text);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void DiagramPack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange: a pack with no policy to hand its tools
        var pack = new DiagramPack();

        // Act / Assert: a family with no policy cannot be created
        Assert.Throws<ArgumentNullException>(() => pack.CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves the policy the composer supplies governs the render tool's destination.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramPack_CreateTools_SuppliedPolicy_GovernsTheRenderTool()
    {
        // Arrange: a policy permitting writes only inside one folder
        using var fixture = new TempDirectoryFixture();
        var render = new DiagramPack().CreateTools(RenderSamples.Policy(fixture.Root))
            .Single(tool => tool.Name == DiagramRenderTool.ToolName);

        // Act: save once inside and once outside
        var inside = await render.InvokeAsync(
            new AIFunctionArguments { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["save_as"] = "in.png" },
            TestContext.Current.CancellationToken);
        var outside = await render.InvokeAsync(
            new AIFunctionArguments { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["save_as"] = "../outside/out.png" },
            TestContext.Current.CancellationToken);

        // Assert: the supplied policy decided both
        Assert.IsType<List<AIContent>>(inside);
        Assert.True(File.Exists(Path.Combine(fixture.Root, "in.png")));
        Assert.StartsWith("Denied (PathNotPermitted)", Assert.IsType<string>(outside), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the instructions name the offered languages and follow the preview mode.
    /// </summary>
    [Fact]
    public void DiagramPack_Instructions_NameTheLanguagesAndFollowThePreviewMode()
    {
        // Arrange: packs offering two languages, in each mode
        var options = new DiagramPackOptions { Formats = ["mermaid", "vega-lite"] };

        // Act: read the instructions
        var image = new DiagramPack(DiagramPreview.Image, options).Instructions;
        var text = new DiagramPack(DiagramPreview.Text, options).Instructions;

        // Assert: both name the languages and the help tool; only text preview requires save_as
        Assert.Contains("mermaid, vega-lite", image, StringComparison.Ordinal);
        Assert.Contains("mermaid, vega-lite", text, StringComparison.Ordinal);
        Assert.Contains(DiagramHelpTool.ToolName, image, StringComparison.Ordinal);
        Assert.Contains("look at the picture", image, StringComparison.Ordinal);
        Assert.Contains("save_as .png path", text, StringComparison.Ordinal);
        Assert.Contains("description of the picture", text, StringComparison.Ordinal);
        Assert.DoesNotContain("plantuml", image, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the instructions are not injected into the tools: no tool description carries them.
    /// </summary>
    [Fact]
    public void DiagramPack_Instructions_AreNotInjectedIntoTheTools()
    {
        // Arrange: a pack and its tools
        var pack = new DiagramPack();
        var tools = pack.CreateTools(new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]));

        // Act / Assert: no description contains the paragraph
        Assert.All(tools, tool => Assert.DoesNotContain(pack.Instructions, tool.Description, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the warm-up completes and the pack still draws afterwards.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramPack_WarmUpAsync_Pack_CompletesAndTheRenderToolStillDraws()
    {
        // Arrange: a pack offering two languages, and its render tool
        var pack = new DiagramPack(DiagramPreview.Image, new DiagramPackOptions { Formats = ["mermaid", "svg"] });
        var render = pack.CreateTools(new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]))
            .Single(tool => tool.Name == DiagramRenderTool.ToolName);

        // Act: warm up, then draw
        await pack.WarmUpAsync(TestContext.Current.CancellationToken);
        var result = await render.InvokeAsync(
            new AIFunctionArguments { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid },
            TestContext.Current.CancellationToken);

        // Assert: the pack draws as before
        Assert.IsType<List<AIContent>>(result);
    }
}
