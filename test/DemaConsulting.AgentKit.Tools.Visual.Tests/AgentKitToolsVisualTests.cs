using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Image;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests;

/// <summary>
///     System-level integration tests for the AgentKitToolsVisual system.
/// </summary>
public class AgentKitToolsVisualTests
{
    /// <summary>
    ///     Proves that the package composes its tool families through the AgentKitCore contract,
    ///     and that an empty composition — before any family is attached — yields no tools.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_EmptyBuilder_ContributesNoTools()
    {
        // Arrange: a policy governing an otherwise empty composition
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: build the tool list before any family has been attached
        var tools = new ToolPackBuilder(policy).Build();

        // Assert: an empty composition contributes no tools
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that attaching the Image pack to a vision host contributes the image family to
    ///     a composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_ImagePack_ContributesTheImageFamily()
    {
        // Arrange: a vision host governing a composition with the image family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: each of the family's tools is published, under the family prefix
        Assert.Equal(
            ["image_read", "image_crop", "image_auto_crop", "image_view"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a host that does not declare vision receives none of the image family's
    ///     tools, so a model that cannot see is not offered content it could only fabricate
    ///     around.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_ImagePackWithoutVision_ContributesNoTools()
    {
        // Arrange: a host that declares no capability, governing a composition with the image
        // family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the vision requirement is unmet, so the family contributes nothing
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that the image family composed over a policy that permits no writing anywhere
    ///     still publishes each of its tools, because each can succeed without writing.
    /// </summary>
    /// <remarks>
    ///     The exact ordered list is asserted rather than set membership, because both which tools
    ///     survive and the order a model sees them in are observable and are part of the contract.
    /// </remarks>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_ReadOnlyPolicy_PublishesEveryImageTool()
    {
        // Arrange: every grant is read-only, on a vision host so the capability gate is satisfied
        // and the policy is the only filter in play
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the four image tools are published, in the family's order
        Assert.Equal(
            ["image_read", "image_crop", "image_auto_crop", "image_view"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the diagram pack chosen for a vision host contributes the diagram
    ///     family in image preview, so the model is shown the pictures it draws.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_DiagramPack_VisionHost_ContributesImagePreview()
    {
        // Arrange: a vision host with the diagram family chosen for it
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var pack = DiagramPack.For(HostCapabilities.Vision);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(pack);

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: both diagram tools, with the render tool returning pictures
        Assert.Equal(DiagramPreview.Image, pack.Preview);
        Assert.Equal(["diagram_help", "diagram_render"], tools.Select(tool => tool.Name));
        Assert.Contains("returns the picture", tools[1].Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a host that cannot see still receives the diagram family, in text preview,
    ///     when the pack is chosen for it.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_DiagramPack_NonVisionHost_ContributesTextPreview()
    {
        // Arrange: a host declaring no capability, with the diagram family chosen for it
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var pack = DiagramPack.For(HostCapabilities.None);
        var builder = new ToolPackBuilder(policy).Add(pack);

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: both diagram tools, with the render tool describing the picture in words
        Assert.Equal(DiagramPreview.Text, pack.Preview);
        Assert.Equal(["diagram_help", "diagram_render"], tools.Select(tool => tool.Name));
        Assert.Contains("returns a description of it", tools[1].Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the text-preview diagram family composed over a policy that permits no
    ///     writing withholds the render tool, whose only outcome there would be a refusal, and keeps
    ///     the help tool.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_SystemComposition_DiagramPack_ReadOnlyTextPreview_WithholdsRender()
    {
        // Arrange: every grant read-only, on a host that cannot see
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);
        var builder = new ToolPackBuilder(policy).Add(DiagramPack.For(HostCapabilities.None));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: only the help tool survives
        Assert.Equal(["diagram_help"], tools.Select(tool => tool.Name));
    }
    /// <summary>
    ///     Proves that an image tool's path denial discloses no host absolute path: the refusal
    ///     names the permitted location relative to the working directory, and neither the working
    ///     directory nor the temporary base it sits in appears.
    /// </summary>
    /// <remarks>
    ///     Each path-taking image tool passes the access policy's denial through unchanged, so the
    ///     scenarios here are the system-level evidence that a refusal adds no host location the
    ///     model was not already given.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitToolsVisual_PathDenials_DiscloseNoHostAbsolutePath()
    {
        // Arrange: a read-only workspace with the image family composed on a vision host
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Outside, "x.png", "not really an image");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();

        // Act: each path-taking tool is asked for a location outside the grant, the way a model
        // escapes
        var denials = new[]
        {
            await Invoke(tools, ImageReadTool.ToolName, new() { ["path"] = "../outside/x.png" }),
            await Invoke(
                tools,
                ImageCropTool.ToolName,
                new() { ["path"] = "../outside/x.png", ["x"] = 0, ["y"] = 0, ["width"] = 1, ["height"] = 1 }),
            await Invoke(tools, ImageAutoCropTool.ToolName, new() { ["path"] = "../outside/x.png" }),
            await Invoke(tools, ImageViewTool.ToolName, new() { ["path"] = "../outside/x.png" })
        };

        // Act: the diagram family's one path, a destination, is named outside a writable grant
        var diagramTools = new ToolPackBuilder(new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new DiagramPack())
            .Build();
        var saveDenial = await Invoke(
            diagramTools,
            DiagramRenderTool.ToolName,
            new() { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["save_as"] = "../outside/x.png" });

        // Assert: each is a path denial naming the grant relatively, with no host absolute path
        var workingDirectory = RealPathResolver.Resolve(fixture.Root);
        var temporaryBase = Path.GetTempPath().TrimEnd('\\', '/');
        Assert.All(denials, denial =>
        {
            Assert.StartsWith("Denied (PathNotPermitted)", denial, StringComparison.Ordinal);
            Assert.Contains("  - . (read-only)", denial, StringComparison.Ordinal);
            Assert.DoesNotContain(workingDirectory, denial, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(temporaryBase, denial, StringComparison.OrdinalIgnoreCase);
        });
        Assert.StartsWith("Denied (PathNotPermitted)", saveDenial, StringComparison.Ordinal);
        Assert.Contains("  - . (read-write)", saveDenial, StringComparison.Ordinal);
        Assert.DoesNotContain(workingDirectory, saveDenial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(temporaryBase, saveDenial, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves that a file a viewed document references from outside the grants is never drawn,
    ///     whichever route the reference takes.
    /// </summary>
    /// <remarks>
    ///     The rendering library judges no input itself and reads any local file by default, so
    ///     this is the system-level evidence that the access policy reaches every file a render
    ///     opens: a parent-folder escape, an absolute path and a link inside the grant pointing
    ///     outside it are each refused, while a picture inside the grant is drawn.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitToolsVisual_ReferencedFiles_OutsideTheGrants_AreNeverDrawn()
    {
        // Arrange: a picture on each side of the grant, a link inside it pointing out, and one
        // document per route
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(20, 20));
        var secret = TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "jn"), fixture.Outside);
        TempDirectoryFixture.WriteFile(fixture.Root, "inside.svg", RenderSamples.SvgWithImage("ok.png"));
        TempDirectoryFixture.WriteFile(fixture.Root, "escape.svg", RenderSamples.SvgWithImage("../outside/secret.png"));
        TempDirectoryFixture.WriteFile(fixture.Root, "absolute.svg", RenderSamples.SvgWithImage(secret));
        TempDirectoryFixture.WriteFile(fixture.Root, "linked.svg", RenderSamples.SvgWithImage("jn/secret.png"));
        var tools = new ToolPackBuilder(new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();
        var view = tools.Single(tool => tool.Name == ImageViewTool.ToolName);

        // Act: view each document
        var inside = await view.InvokeAsync(new() { ["path"] = "inside.svg" }, TestContext.Current.CancellationToken);
        var refused = new List<object?>();
        foreach (var name in new[] { "escape.svg", "absolute.svg", "linked.svg" })
        {
            refused.Add(await view.InvokeAsync(new() { ["path"] = name }, TestContext.Current.CancellationToken));
        }

        // Assert: the inside picture is drawn; every escape fails the view instead
        Assert.IsType<List<AIContent>>(inside);
        Assert.All(refused, result => Assert.StartsWith(
            "Render failed (file-access-denied",
            Assert.IsType<string>(result),
            StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves that a file inline diagram source references from outside the grants is never
    ///     drawn, while one inside the grant is.
    /// </summary>
    /// <remarks>
    ///     Inline source has no folder of its own; its references resolve against the working
    ///     directory and are judged by the same mapped policy as a viewed file's.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitToolsVisual_ReferencedFiles_InlineDiagramOutsideTheGrants_IsNeverDrawn()
    {
        // Arrange: a picture on each side of the grant, and the diagram family on a vision host
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(20, 20));
        var secret = TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        var render = new ToolPackBuilder(new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new DiagramPack())
            .Build()
            .Single(tool => tool.Name == DiagramRenderTool.ToolName);

        // Act: draw an SVG embedding each picture, the outside one by two routes
        var inside = await render.InvokeAsync(
            new() { ["format"] = "svg", ["source"] = RenderSamples.SvgWithImage("ok.png") },
            TestContext.Current.CancellationToken);
        var refused = new List<object?>();
        foreach (var href in new[] { "../outside/secret.png", secret })
        {
            refused.Add(await render.InvokeAsync(
                new() { ["format"] = "svg", ["source"] = RenderSamples.SvgWithImage(href) },
                TestContext.Current.CancellationToken));
        }

        // Assert: the inside picture is drawn; each escape fails the render, and the relative one
        // names no host path the model did not write
        Assert.IsType<List<AIContent>>(inside);
        Assert.All(refused, result => Assert.StartsWith(
            "Render failed (file-access-denied",
            Assert.IsType<string>(result),
            StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Outside, Assert.IsType<string>(refused[0]), StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>
    ///     Proves that a render never fetches anything from the network.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitToolsVisual_Rendering_NetworkReferences_AreNeverFetched()
    {
        // Arrange: a listener counting every request, and documents of two formats referencing it
        using var fixture = new TempDirectoryFixture();
        using var listener = CountingListener.Start();
        TempDirectoryFixture.WriteFile(fixture.Root, "figure.svg", RenderSamples.SvgWithImage(listener.Url + "x.png"));
        TempDirectoryFixture.WriteFile(fixture.Root, "sequence.puml", RenderSamples.PlantUmlInclude(listener.Url + "inc.puml"));
        var tools = new ToolPackBuilder(new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();
        var view = tools.Single(tool => tool.Name == ImageViewTool.ToolName);

        // Act: view both
        await view.InvokeAsync(new() { ["path"] = "figure.svg" }, TestContext.Current.CancellationToken);
        await view.InvokeAsync(new() { ["path"] = "sequence.puml" }, TestContext.Current.CancellationToken);

        // Assert: nothing reached the listener
        Assert.Equal(0, listener.Hits);
    }

    /// <summary>
    ///     Invokes the named tool from a composed list and returns its text result.
    /// </summary>
    /// <param name="tools">The composed list.</param>
    /// <param name="name">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The text the tool returned.</returns>
    private static async Task<string> Invoke(IReadOnlyList<AIFunction> tools, string name, AIFunctionArguments arguments)
    {
        var result = await tools.Single(tool => tool.Name == name).InvokeAsync(arguments, TestContext.Current.CancellationToken);
        return Assert.IsType<string>(result);
    }
}
