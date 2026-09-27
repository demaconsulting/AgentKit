using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileCreateDirectoryTool"/> class.
/// </summary>
/// <remarks>
///     The two success paths — created, and already present — are verified separately and are
///     asserted to report different things, because the whole justification for treating an
///     existing directory as a success is that the model can still tell which happened.
/// </remarks>
public class FileCreateDirectoryToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileCreateDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_create_directory", FileCreateDirectoryTool.ToolName);
        Assert.StartsWith(
            FilePack.FamilyPrefix + "_", FileCreateDirectoryTool.ToolName, StringComparison.Ordinal);

        ToolName.Validate(FileCreateDirectoryTool.ToolName);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileCreateDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileCreateDirectoryTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted directory is created.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_PermittedPath_CreatesIt()
    {
        // Arrange: a workspace with nothing in it, addressed by a bare relative name
        using var fixture = new TempDirectoryFixture();
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: create one directory
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "reports" });

        // Assert: it exists, and the result says it was created
        var text = Assert.IsType<string>(result);
        Assert.Contains("Created the directory", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "reports")));
    }

    /// <summary>
    ///     Proves every missing directory above the named one is created too.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_NestedPath_CreatesEveryMissingParent()
    {
        // Arrange: a workspace in which neither the named directory nor its parents exist
        using var fixture = new TempDirectoryFixture();
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: name a path two levels below anything that exists
        var result = await InvokeAsync(
            tool, new AIFunctionArguments { ["path"] = "reports/drafts" });

        // Assert: both levels exist, so the model need not create them one at a time
        Assert.IsType<string>(result);
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "reports")));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "reports", "drafts")));
    }

    /// <summary>
    ///     Proves an existing directory is a success reported in its own words, with nothing
    ///     beneath it disturbed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_ExistingDirectory_ReportsItExistedAndLeavesContents()
    {
        // Arrange: a directory that already exists and already holds a file
        using var fixture = new TempDirectoryFixture();
        var existing = Path.Combine(fixture.Root, "reports");
        var inside = TempDirectoryFixture.WriteFile(existing, "kept.txt", "content");
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for it again
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "reports" });

        // Assert: a success that says it already existed — not the created text — and the file
        // it held is untouched
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.Contains("already exists", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Created the directory", text, StringComparison.Ordinal);
        Assert.Equal(
            "content",
            await System.IO.File.ReadAllTextAsync(inside, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves an existing file is never replaced by a directory.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_ExistingFile_ReturnsDenialAndLeavesTheFile()
    {
        // Arrange: a file occupying the name the request will use
        using var fixture = new TempDirectoryFixture();
        var file = TempDirectoryFixture.WriteFile(fixture.Root, "reports", "content");
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a directory of the same name
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "reports" });

        // Assert: refused, and the file still holds its content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal(
            "content",
            await System.IO.File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a creation outside the write grant is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_ReadOnlyLocation_ReturnsDenialAndCreatesNothing()
    {
        // Arrange: a readable but unwritable root, so nothing may be created in it
        using var fixture = new TempDirectoryFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = FileCreateDirectoryTool.Create(policy);

        // Act: attempt a creation within the read-only location
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "reports" });

        // Assert: refused, and nothing was created — read access never implies write access
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "reports")));
    }

    /// <summary>
    ///     Proves a request whose missing parents reach above every permitted location is refused
    ///     and creates nothing at all.
    /// </summary>
    /// <remarks>
    ///     A grant root need not exist — path resolution is lexical and requires no component to
    ///     be present — so a grant rooted at a location whose own parents are missing would have
    ///     had those parents materialized by a single creation call, writing where no grant
    ///     reaches. The refusal is total rather than partial because the two candidate rules
    ///     coincide: a directory cannot be created inside a parent that does not exist, so
    ///     creating only the permitted part would still have to stop here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_MissingParentAboveTheGrantRoot_ReturnsDenialAndCreatesNothing()
    {
        // Arrange: a grant rooted two levels below anything that exists, so honoring the request
        // would materialize a directory above the grant root that no grant covers
        using var fixture = new TempDirectoryFixture();
        var ungranted = Path.Combine(fixture.Outside, "absent");
        var grantRoot = Path.Combine(ungranted, "grant-root");
        var policy = new PathPolicy(grantRoot, [PathRule.ReadWrite(grantRoot)]);
        var tool = FileCreateDirectoryTool.Create(policy);

        // Act: name a directory inside the grant, whose creation drags its ancestors with it
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "child" });

        // Assert: refused, disclosing no host location, and nothing at any level was created
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(ungranted));
        Assert.False(Directory.Exists(grantRoot));
        Assert.False(Directory.Exists(Path.Combine(grantRoot, "child")));
    }

    /// <summary>
    ///     Proves a grant root that does not yet exist is still created, so the refusal above is
    ///     about ancestors the grant does not cover rather than about missing ancestors at all.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCreateDirectoryTool_CreateDirectory_MissingGrantRootWhoseParentExists_CreatesBothLevels()
    {
        // Arrange: a grant rooted at a location that does not exist, but whose own parent does
        using var fixture = new TempDirectoryFixture();
        var grantRoot = Path.Combine(fixture.Root, "grant-root");
        var policy = new PathPolicy(grantRoot, [PathRule.ReadWrite(grantRoot)]);
        var tool = FileCreateDirectoryTool.Create(policy);

        // Act: name a directory inside the grant
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "child" });

        // Assert: created, root and all — the grant root is itself a permitted location, so
        // materializing it is a write the policy approved
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(grantRoot));
        Assert.True(Directory.Exists(Path.Combine(grantRoot, "child")));
    }

    /// <summary>
    ///     Proves the tool's description tells the model both outcomes are reported separately.
    /// </summary>
    [Fact]
    public void FileCreateDirectoryTool_Create_Description_NamesBothOutcomes()
    {
        // Arrange / Act: build the tool a composition would publish
        using var fixture = new TempDirectoryFixture();
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Assert: the declaration a model reads states the idempotent behavior, which is the only
        // way it can know a repeat call is safe
        var description = tool.Description;
        Assert.Contains("missing parent directories", description, StringComparison.Ordinal);
        Assert.Contains("already existed", description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the tool's description declares that parent creation stops at the permitted
    ///     locations.
    /// </summary>
    /// <remarks>
    ///     A model choosing this tool reads only the description. Declaring that missing parents
    ///     are created "along the way" without declaring where that stops would invite a request
    ///     whose only possible outcome is a refusal the model could not have predicted.
    /// </remarks>
    [Fact]
    public void FileCreateDirectoryTool_Create_Description_DeclaresTheParentBoundary()
    {
        // Arrange / Act: build the tool a composition would publish
        using var fixture = new TempDirectoryFixture();
        var tool = FileCreateDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Assert: the declaration states that a parent outside a permitted location is refused,
        // and that such a refusal creates nothing
        var description = tool.Description;
        Assert.Contains("outside every permitted location", description, StringComparison.Ordinal);
        Assert.Contains("creating nothing at all", description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Composes a policy over one read-write location.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Invokes a tool with the supplied arguments, exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
