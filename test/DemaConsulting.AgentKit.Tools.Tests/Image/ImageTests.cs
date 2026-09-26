using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Subsystem-level integration tests for the Image tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy and a declared host
///     capability, then invoked through the published tool list. They assert the properties that
///     belong to the family as a whole — one prefix, one policy, refusals that are results, and
///     the two properties this family exists to guarantee: that image content survives to the
///     caller and that a host without vision is never even asked for the family — rather than any
///     single unit's algorithm, which its own unit tests cover.
/// </remarks>
public class ImageTests
{
    /// <summary>
    ///     One byte sequence standing in for image content; the tool does not parse it.
    /// </summary>
    private static readonly byte[] SampleBytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04];

    /// <summary>
    ///     Proves a composition attaching the family, on a vision host, publishes both tools.
    /// </summary>
    [Fact]
    public void Image_Family_ComposedThroughBuilder_PublishesBothTools()
    {
        // Arrange: a vision host with the family attached under one policy
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: both tools the family promises, under the one family prefix
        Assert.Equal(
            [ImageReadTool.ToolName, ImageCropTool.ToolName],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a host declaring vision receives the family.
    /// </summary>
    [Fact]
    public void Image_Family_HostDeclaringVision_ReceivesTheFamily()
    {
        // Arrange: a host that declares the vision capability the family requires
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: the declared capability is met, so the family is registered
        Assert.NotEmpty(tools);
    }

    /// <summary>
    ///     Proves a host that does not declare vision receives no tools from the family.
    /// </summary>
    [Fact]
    public void Image_Family_HostWithoutVision_ReceivesNoTools()
    {
        // Arrange: a host that declares nothing, so the vision requirement is unmet
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new ImagePack());

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: a model that cannot see is offered none of the family's tools
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves the composition never even asks the pack to create tools on a non-vision host.
    /// </summary>
    /// <remarks>
    ///     An empty tool list alone cannot distinguish a pack that was consulted and returned
    ///     nothing from one that was never consulted. The recording decorator makes the
    ///     distinction observable, and the gate's promise is the second: the pack is not asked at
    ///     all, so a model without vision is never offered a tool that returns content it cannot
    ///     use and would then describe from nothing.
    /// </remarks>
    [Fact]
    public void Image_Family_HostWithoutVision_PackIsNotConsulted()
    {
        // Arrange: the real pack wrapped so its consultation can be observed, on a host that
        // declares no capability
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var probe = new RecordingToolPack(new ImagePack());
        var builder = new ToolPackBuilder(policy).Add(probe);

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: the pack was never asked for its tools, and none were contributed
        Assert.False(probe.WasConsulted);
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void Image_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        // Arrange: the family composed under one policy on a vision host
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();

        // Act / Assert: every name survives the convention's own validation, and no tool is
        // offered to a model without a description it can choose by
        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(ImagePack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves a family tool's image content reaches the caller in the form the tool returned
    ///     it, not as serialized JSON.
    /// </summary>
    /// <remarks>
    ///     This is the family's reason to exist, asserted through the tool as an application
    ///     actually attaches it. Without the guarded construction path the content would arrive
    ///     as a <see cref="JsonElement"/>, the provider would never receive the image, and the
    ///     model would fabricate a description of content it never saw.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_ToolResult_ReachesTheCallerUnserialized()
    {
        // Arrange: the family composed over a permitted location holding a known image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: read the image through the composed tool
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: real content, not a JSON wrapping of it
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.IsNotType<JsonElement>(result);
        Assert.Equal(SampleBytes, Assert.IsType<DataContent>(content[1]).Data.ToArray());
    }

    /// <summary>
    ///     Proves a permitted image is returned as image content carrying its media type.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_PermittedImage_IsReturnedAsImageContent()
    {
        // Arrange: the family composed over a permitted location holding a known image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: read the image
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: a caption followed by the image, carrying the media type and the real bytes
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);
        Assert.Equal(SampleBytes, data.Data.ToArray());
    }

    /// <summary>
    ///     Proves the family states an image's pixel dimensions in the caption it returns.
    /// </summary>
    /// <remarks>
    ///     A model can see a picture but cannot measure one. Stating the size alongside the
    ///     content is what gives it a coordinate space to reason about a region of that image in.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_ReadCaption_StatesTheImagesPixelDimensions()
    {
        // Arrange: the family composed over a permitted location holding a real image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", ImageTestImages.Png(53, 29));
        var tools = Compose(fixture.Root);

        // Act: read the image through the composed family
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: the caption names the real declared size
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]);
        Assert.Contains("53x29 pixels", caption.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the family returns a cropped region as image content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_CroppedRegion_IsReturnedAsImageContent()
    {
        // Arrange: the family composed over a permitted location holding a real image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", ImageTestImages.Png(40, 30));
        var tools = Compose(fixture.Root);

        // Act: ask for a region through the composed family
        var result = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 4,
                ["y"] = 5,
                ["width"] = 12,
                ["height"] = 9
            });

        // Assert: the region comes back as image content, not as a written file or a JSON copy
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);

        var decoded = ImageTestImages.Decode(data.Data.ToArray());
        Assert.Equal(12, decoded.Width);
        Assert.Equal(9, decoded.Height);
    }

    /// <summary>
    ///     Proves the size the read tool reports is the size a region request is aimed with.
    /// </summary>
    /// <remarks>
    ///     <b>The increment's thesis, in one scenario.</b> The family states an image's size, and
    ///     the region named within exactly that size is accepted — while one pixel beyond it is
    ///     refused. The two tools are one capability: a region request the model cannot aim is a
    ///     region request it will aim wrongly.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_ReadCaption_StatesTheDimensionsACropCanBeAimedWith()
    {
        // Arrange: the family composed over a permitted location holding a real image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", ImageTestImages.Png(36, 22));
        var tools = Compose(fixture.Root);

        // Act: read the image, then ask for exactly the region its reported size allows, and
        // then for one pixel more
        var readResult = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });
        var wholeResult = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 36,
                ["height"] = 22
            });
        var beyondResult = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 37,
                ["height"] = 22
            });

        // Assert: the caption states the size, the region within it is content, and the region
        // beyond it is a refusal naming that same size
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(readResult)[0]);
        Assert.Contains("36x22 pixels", caption.Text, StringComparison.Ordinal);
        Assert.IsType<List<AIContent>>(wholeResult);
        Assert.Contains("36x22 pixels", Assert.IsType<string>(beyondResult), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an image declaring more pixels than the host's budget is refused before it is
    ///     decoded.
    /// </summary>
    /// <remarks>
    ///     The fixture carries no pixel data at all, so a family that decoded before triaging
    ///     would refuse it as undecodable instead of as oversized. Asserting the oversized
    ///     refusal is therefore evidence the decision was made from the header alone.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_OversizedDeclaredImage_IsRefusedBeforeDecoding()
    {
        // Arrange: the family composed over a permitted location holding a bomb header
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "bomb.png", ImageTestImages.PngHeaderOnly(8000, 8000));
        var tools = Compose(fixture.Root);

        // Act: ask for a small region of it
        var result = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 8,
                ["height"] = 8
            });

        // Assert: refused for its declared size, and not for being undecodable
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be decoded", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path outside the permitted location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_PathOutsideRoot_IsRefused()
    {
        // Arrange: the family composed over a permitted location, and an image outside it
        using var fixture = new TempDirectoryFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: request the file outside the permitted location
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = outsideFile });

        // Assert: refused on its real location
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(result),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refused request returns a result rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_DeniedRequest_ReturnsAResultWithoutThrowing()
    {
        // Arrange: the family composed over a permitted location, and a path outside it
        using var fixture = new TempDirectoryFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: request the refused file
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = outsideFile });

        // Assert: a returned refusal naming its reason, because an exception would end the turn
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every refusal the family produces discloses the permitted location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_DenialText_DisclosesPermittedLocation()
    {
        // Arrange: the family composed over a permitted location, and a path outside it
        using var fixture = new TempDirectoryFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var tools = Compose(fixture.Root);
        var permitted = RealPathResolver.Resolve(fixture.Root);

        // Act: request the refused file
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = outsideFile });

        // Assert: the refusal names the permitted location so the model can re-address
        var text = Assert.IsType<string>(result);
        Assert.Contains(outsideFile, text, StringComparison.Ordinal);
        Assert.Contains(permitted, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unsupported type is refused with a redirect where a better tool exists.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_UnsupportedType_IsRefusedWithRedirectWhereUseful()
    {
        // Arrange: the family composed over a permitted location holding a vector-text file
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.svg", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: request the .svg file
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: refused as an unsupported type and pointed at the tool that can read it
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file beyond the policy's binary ceiling is refused rather than truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_FileBeyondTheBinaryCeiling_IsRefusedNotTruncated()
    {
        // Arrange: the family composed under a policy carrying a sixteen-byte binary ceiling
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "big.png", new byte[128]);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root)],
            new ToolLimits(maxBinaryBytes: 16));
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();

        // Act: read the oversized file
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: a refusal naming the ceiling, never content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("16-byte", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path stated the way a model states it is resolved against the workspace.
    /// </summary>
    /// <remarks>
    ///     The image family shares the same access policy the text file family does, so a bare
    ///     name a listing reported is directly usable here. An image family that read names
    ///     differently would make a discovered name unusable.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_RelativePathFromAModel_IsResolvedAgainstTheWorkspace()
    {
        // Arrange: the family composed over a workspace holding one image
        using var fixture = new TempDirectoryFixture();
        WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tools = Compose(fixture.Root);

        // Act: read the image by name alone
        var result = await InvokeAsync(
            tools,
            ImageReadTool.ToolName,
            new AIFunctionArguments { ["path"] = "picture.png" });

        // Assert: the real bytes, reached from the workspace
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(SampleBytes, Assert.IsType<DataContent>(content[1]).Data.ToArray());
    }

    /// <summary>
    ///     Proves the family writes a cropped region to a destination a read-write grant permits
    ///     and confirms it in text.
    /// </summary>
    /// <remarks>
    ///     The composed counterpart of the unit's write scenario: the capability is reached
    ///     through the pack the application attaches, under the one policy the application
    ///     configured, rather than through a tool constructed in isolation.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_CroppedRegion_WrittenToAPermittedDestination_ConfirmsInText()
    {
        // Arrange: the family composed over a permitted location holding a real image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", ImageTestImages.Png(40, 30));
        var tools = Compose(fixture.Root);

        // Act: ask for a region and name where to produce it
        var result = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 4,
                ["y"] = 5,
                ["width"] = 12,
                ["height"] = 9,
                ["destination"] = "figure.png"
            });

        // Assert: a confirmation naming the file, and a real PNG of that region on disk
        var text = Assert.IsType<string>(result);
        Assert.Contains("figure.png", text, StringComparison.Ordinal);

        var decoded = ImageTestImages.Decode(
            await System.IO.File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, "figure.png"), TestContext.Current.CancellationToken));
        Assert.Equal(12, decoded.Width);
        Assert.Equal(9, decoded.Height);
    }

    /// <summary>
    ///     Proves one composed call is judged by both of the policy's decisions independently.
    /// </summary>
    /// <remarks>
    ///     <b>The increment's thesis, in one scenario.</b> The image is admitted by the read
    ///     decision and the destination beside it refused by the write decision, in a single
    ///     call — which is what keeps a read-wide, write-narrow configuration meaningful rather
    ///     than decorative, and it is exactly the configuration an application that grants a
    ///     read-only workspace and a writable session folder produces.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Image_Family_CropDestinationUnderAReadOnlyGrant_IsRefusedNamingTheWritableLocation()
    {
        // Arrange: the family composed over a read-only workspace and a writable session folder
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", ImageTestImages.Png(40, 30));
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();

        // Act: read the image the workspace grant admits, and try to write beside it
        var result = await InvokeAsync(
            tools,
            ImageCropTool.ToolName,
            new AIFunctionArguments
            {
                ["path"] = file,
                ["x"] = 4,
                ["y"] = 5,
                ["width"] = 12,
                ["height"] = 9,
                ["destination"] = "figure.png"
            });

        // Assert: refused by the write decision, naming the location that is writable, with
        // nothing left behind in the location that is not
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains(fixture.Outside, text, StringComparison.Ordinal);
        Assert.Contains("(read-write)", text, StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Root, "figure.png")));
    }

    /// <summary>
    ///     Composes the family under a policy rooted at one location, on a vision host.
    /// </summary>
    /// <param name="root">The permitted read and write location.</param>
    /// <returns>The composed tool list.</returns>
    private static IReadOnlyList<AIFunction> Compose(string root)
    {
        var policy = new PathPolicy(root, [PathRule.ReadWrite(root)]);
        return new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build();
    }

    /// <summary>
    ///     Writes a file with known bytes into a directory of the temporary tree.
    /// </summary>
    /// <param name="directory">The directory to write into; created when it does not exist.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The full path of the written file.</returns>
    private static string WriteBytes(string directory, string fileName, byte[] content)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);
        System.IO.File.WriteAllBytes(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Invokes one composed tool by name, exactly as a runtime would.
    /// </summary>
    /// <param name="tools">The composed tool list.</param>
    /// <param name="name">The name of the tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments arguments)
    {
        var tool = tools.Single(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
