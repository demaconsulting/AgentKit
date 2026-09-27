using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImagePack"/> class.
/// </summary>
/// <remarks>
///     The pack is the only public way to obtain the family's tools, so these scenarios verify
///     what a composing application can observe: the prefix claimed, the capability required,
///     the tool produced, and that the policy the composition supplied is the one governing it.
/// </remarks>
public class ImagePackTests
{
    /// <summary>
    ///     One byte sequence standing in for image content; the tool does not parse it.
    /// </summary>
    private static readonly byte[] SampleBytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04];

    /// <summary>
    ///     Proves the published family prefix constant is the family name.
    /// </summary>
    [Fact]
    public void ImagePack_FamilyPrefix_Constant_IsImage()
    {
        // Arrange / Act: read the published constant
        var prefix = ImagePack.FamilyPrefix;

        // Assert: the family the tool names are qualified by
        Assert.Equal("image", prefix);
    }

    /// <summary>
    ///     Proves the contract reports the same prefix the class publishes as a constant.
    /// </summary>
    [Fact]
    public void ImagePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant()
    {
        // Arrange: the pack seen through the contract a composer uses
        IToolPack pack = new ImagePack();

        // Act: read the prefix through the contract
        var prefix = pack.FamilyPrefix;

        // Assert: the constant and the contract cannot drift apart
        Assert.Equal(ImagePack.FamilyPrefix, prefix);
    }

    /// <summary>
    ///     Proves the family requires the host to be vision-capable.
    /// </summary>
    [Fact]
    public void ImagePack_RequiredCapabilities_Pack_RequiresVision()
    {
        // Arrange: the pack
        var pack = new ImagePack();

        // Act: read what it requires of a host
        var required = pack.RequiredCapabilities;

        // Assert: the family returns image content, so it is gated behind the vision capability
        Assert.Equal(HostCapabilities.Vision, required);
    }

    /// <summary>
    ///     Proves the pack creates the read tool, the crop tool and the auto-crop tool.
    /// </summary>
    /// <remarks>
    ///     The three are asserted together because they are one capability: the read tool reports
    ///     the coordinate space the crop tool consumes, and the auto-crop tool answers the region
    ///     question a model cannot state in that space at all. A pack that published only some of
    ///     them would offer a model a region request it could not aim, a size it could not use,
    ///     or no way to trim a picture it can see is mostly margin.
    /// </remarks>
    [Fact]
    public void ImagePack_CreateTools_Policy_CreatesTheReadCropAndAutoCropTools()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new ImagePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: exactly the three tools the family publishes
        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, tool => tool.Name == ImageReadTool.ToolName);
        Assert.Contains(tools, tool => tool.Name == ImageCropTool.ToolName);
        Assert.Contains(tools, tool => tool.Name == ImageAutoCropTool.ToolName);
    }

    /// <summary>
    ///     Proves the family is published whole under a policy that permits no writing anywhere,
    ///     because every one of its tools can succeed there: reading an image is a read, and both
    ///     region tools return image content rather than writing a file when no destination is
    ///     named.
    /// </summary>
    /// <remarks>
    ///     This records a deliberate decision rather than an incidental outcome. Both region tools
    ///     accept an optional <c>destination</c> that does require a write grant, but the rule a
    ///     pack applies is "could this tool ever succeed", not "could every argument ever succeed".
    ///     Suppressing either would remove its primary, fully-working inline mode; naming a
    ///     destination under a read-only policy earns an ordinary denial that tells the model where
    ///     it could write instead.
    /// </remarks>
    [Fact]
    public void ImagePack_CreateTools_ReadOnlyPolicy_StillPublishesEveryTool()
    {
        // Arrange: every grant is read-only, so nothing anywhere may be written
        var pack = new ImagePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: create the family's tools under that policy
        var tools = pack.CreateTools(policy).ToList();

        // Assert: all three survive — the family is never narrowed by a read-only path policy
        Assert.Equal(3, tools.Count);
        Assert.Contains(tools, tool => tool.Name == ImageReadTool.ToolName);
        Assert.Contains(tools, tool => tool.Name == ImageCropTool.ToolName);
        Assert.Contains(tools, tool => tool.Name == ImageAutoCropTool.ToolName);
    }

    /// <summary>
    ///     Proves the pack honors the contract obligation to return no null tool.
    /// </summary>
    [Fact]
    public void ImagePack_CreateTools_Policy_ReturnsNoNullTool()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new ImagePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: the collection the composer receives contains no hole
        Assert.NotEmpty(tools);
        Assert.All(tools, Assert.NotNull);
    }

    /// <summary>
    ///     Proves every tool the pack creates carries the family prefix it declares.
    /// </summary>
    [Fact]
    public void ImagePack_CreateTools_EveryTool_CarriesTheFamilyPrefix()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new ImagePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: the declaration the prefix collision check depends on is true
        Assert.All(
            tools,
            tool => Assert.StartsWith(
                ImagePack.FamilyPrefix + "_",
                tool.Name,
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void ImagePack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange: a pack with no policy to hand its tools
        var pack = new ImagePack();

        // Act / Assert: a family with no policy cannot be created
        Assert.Throws<ArgumentNullException>(() => pack.CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves the policy the composer supplies is the one governing every created tool.
    /// </summary>
    /// <remarks>
    ///     Asserted against the read tool, the crop tool and the auto-crop tool in turn, because
    ///     a tool that quietly observed a different policy from its neighbor would make the
    ///     configured containment unverifiable — which is the same as not having it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools()
    {
        // Arrange: a pack whose tools are created from a policy rooted at one location
        using var fixture = new TempDirectoryFixture();
        var permitted = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var refused = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);
        var tools = new ImagePack().CreateTools(policy).ToList();
        var readTool = tools.Single(tool => tool.Name == ImageReadTool.ToolName);
        var cropTool = tools.Single(tool => tool.Name == ImageCropTool.ToolName);
        var autoCropTool = tools.Single(tool => tool.Name == ImageAutoCropTool.ToolName);

        // Act: read one path the policy permits and one it does not, then refuse the same path
        // through each sibling tool
        var permittedResult = await InvokeReadAsync(readTool, permitted);
        var refusedResult = await InvokeReadAsync(readTool, refused);
        var cropRefusedResult = await cropTool.InvokeAsync(
            new AIFunctionArguments
            {
                ["path"] = refused,
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 1,
                ["height"] = 1
            },
            TestContext.Current.CancellationToken);
        var autoCropRefusedResult = await InvokeReadAsync(autoCropTool, refused);

        // Assert: the supplied policy governs every decision, whichever tool made it
        var content = Assert.IsType<List<AIContent>>(permittedResult);
        Assert.Equal(SampleBytes, Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(refusedResult),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(cropRefusedResult),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(autoCropRefusedResult),
            StringComparison.Ordinal);
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
    ///     Invokes a read tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeReadAsync(AIFunction tool, string path)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = path },
            TestContext.Current.CancellationToken);
    }
}
