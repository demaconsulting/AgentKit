using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Image;
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
            ["image_read", "image_crop", "image_auto_crop"],
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

        // Assert: the three image tools are published, in the family's order
        Assert.Equal(
            ["image_read", "image_crop", "image_auto_crop"],
            tools.Select(tool => tool.Name));
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
            await Invoke(tools, ImageAutoCropTool.ToolName, new() { ["path"] = "../outside/x.png" })
        };

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
