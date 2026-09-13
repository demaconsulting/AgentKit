using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileCreateTool"/> class.
/// </summary>
public class TextFileCreateToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileCreateTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_create", TextFileCreateTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileCreateTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileCreateTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileCreateTool.Create(null!));
    }

    /// <summary>
    ///     Proves a new file is created with the given content, addressed by a bare relative name,
    ///     and the confirmation reports the created file's total line count.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_NewFile_WritesTheContentAndReportsTheLineCount()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "new.txt", ["content"] = "alpha\nbeta\ngamma\n" });

        // The line count spares the model an exploratory read before its next line-addressed request.
        var text = Assert.IsType<string>(result);
        Assert.Equal("Created the file with 17 characters in 3 lines.", text);
        Assert.Equal("alpha\nbeta\ngamma\n", await ReadAsync(Path.Combine(fixture.Root, "new.txt")));
    }

    /// <summary>
    ///     Proves an empty new file is reported as zero lines rather than one, matching the line
    ///     model every other tool in the family addresses.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_EmptyContent_ReportsZeroLines()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "empty.txt", ["content"] = string.Empty });

        Assert.Equal("Created the file with 0 characters in 0 lines.", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves create refuses to replace a file that already exists, stating the fact and naming
    ///     no other tool, and leaves the existing file unchanged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_ExistingFile_ReturnsDenialAndLeavesItUnchanged()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "existing.txt", "original");
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "existing.txt", ["content"] = "replacement" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReplaceTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        Assert.Equal("original", await ReadAsync(Path.Combine(fixture.Root, "existing.txt")));
    }

    /// <summary>
    ///     Proves the existing-file denial — the case that once instructed a model to run the
    ///     destructive editor a user had forbidden — states the fact and prescribes no remedy: it
    ///     opens no redirect sentence and names no sibling tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_ExistingFile_DenialPrescribesNoRemedy()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "existing.txt", "original");
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "existing.txt", ["content"] = "replacement" });

        var text = Assert.IsType<string>(result);
        Assert.Contains(
            "A file already exists at the requested path.", text, StringComparison.Ordinal);
        // The rule: a denial states a fact and never prescribes a remedy.
        Assert.DoesNotContain("Use the '", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_replace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("text_file_delete", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty content string creates an empty file rather than being refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_EmptyContent_CreatesAnEmptyFile()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "empty.txt", ["content"] = string.Empty });

        Assert.IsType<string>(result);
        Assert.Equal(string.Empty, await ReadAsync(Path.Combine(fixture.Root, "empty.txt")));
    }

    /// <summary>
    ///     Proves a missing content argument is refused rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_MissingContent_ReturnsDenialWithoutThrowing()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "new.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing parent directory is refused rather than materialized.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_MissingParentDirectory_ReturnsDenial()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileCreateTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "absent-dir/new.txt", ["content"] = "x" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "absent-dir")));
    }

    /// <summary>
    ///     Proves creation in a read-only location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileCreateTool_Create_ReadOnlyLocation_ReturnsDenial()
    {
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = TextFileCreateTool.Create(policy);

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "new.txt", ["content"] = "x" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
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
    ///     Reads a file's text with the test's cancellation token.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file's text.</returns>
    private static async Task<string> ReadAsync(string path)
    {
        return await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
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
