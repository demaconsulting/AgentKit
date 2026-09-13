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
    ///     Proves the pack creates the read tool.
    /// </summary>
    [Fact]
    public void ImagePack_CreateTools_Policy_CreatesTheReadTool()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new ImagePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: exactly the read tool the family publishes today
        Assert.Single(tools);
        Assert.Equal(ImageReadTool.ToolName, tools[0].Name);
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
    ///     Proves the policy the composer supplies is the one governing the created tools.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools()
    {
        // Arrange: a pack whose tools are created from a policy rooted at one location
        using var fixture = new ReparsePointFixture();
        var permitted = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var refused = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);
        var readTool = new ImagePack().CreateTools(policy).First();

        // Act: read one path the policy permits and one it does not
        var permittedResult = await InvokeReadAsync(readTool, permitted);
        var refusedResult = await InvokeReadAsync(readTool, refused);

        // Assert: the supplied policy governs both decisions
        var content = Assert.IsType<List<AIContent>>(permittedResult);
        Assert.Equal(SampleBytes, Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(refusedResult),
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
