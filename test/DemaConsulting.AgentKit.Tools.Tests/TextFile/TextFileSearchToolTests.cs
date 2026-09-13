using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileSearchTool"/> class.
/// </summary>
/// <remarks>
///     The scenarios use the paths a model actually sends and assert the grep-style output. The
///     most important scenario proves a file reachable only through a link outside the grants is
///     never surfaced by a search — the single most important security property of this increment.
/// </remarks>
public class TextFileSearchToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileSearchTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("text_file_search", TextFileSearchTool.ToolName);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", TextFileSearchTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileSearchTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileSearchTool.Create(null!));
    }

    /// <summary>
    ///     Proves a match is reported grep-style as path:line:content, in the caller's dialect.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_Match_ReportsPathLineContent()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(
            fixture.Root, "client.cs", "using System;\nprivate const int RetryLimit = 3;\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "RetryLimit" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("client.cs:2:private const int RetryLimit = 3;", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves context lines are shown with a dash separator around a colon-separated match.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_WithContextLines_ShowsSurroundingLines()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "before\ntarget\nafter\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "target", ["contextLines"] = 1 });

        var text = Assert.IsType<string>(result);
        Assert.Contains("note.txt-1-before", text, StringComparison.Ordinal);
        Assert.Contains("note.txt:2:target", text, StringComparison.Ordinal);
        Assert.Contains("note.txt-3-after", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the search is literal by default, so a regex metacharacter matches itself.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_LiteralByDefault_MatchesMetacharactersAsText()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "a.b\n" + "axb\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        // "a.b" as a literal matches only "a.b", not "axb"
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "a.b" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("note.txt:1:a.b", text, StringComparison.Ordinal);
        Assert.DoesNotContain("axb", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a regular-expression search matches when literal is false.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_RegexMode_MatchesTheExpression()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "a.b\n" + "axb\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "a.b", ["literal"] = false });

        var text = Assert.IsType<string>(result);
        Assert.Contains("note.txt:1:a.b", text, StringComparison.Ordinal);
        Assert.Contains("note.txt:2:axb", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an invalid regular expression is a returned refusal, not a thrown error.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_InvalidRegex_ReturnsDenialWithoutThrowing()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "(unterminated", ["literal"] = false });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves maxMatches caps the number of reported matches.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_MaxMatches_CapsTheReportedMatches()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "hit\nhit\nhit\nhit\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "hit", ["maxMatches"] = 2 });

        var text = Assert.IsType<string>(result);
        var matchLines = text.Split('\n').Count(line => line.Contains(":hit", StringComparison.Ordinal));
        Assert.Equal(2, matchLines);
    }

    /// <summary>
    ///     Proves a search that matches nothing is a fact, not a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_NoMatch_ReturnsNoMatchesNotADenial()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "nothing here\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "absent" });

        var text = Assert.IsType<string>(result);
        Assert.Equal("No matches.", text);
    }

    /// <summary>
    ///     Proves a search over an empty directory returns no matches without a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_EmptyDirectory_ReturnsNoMatches()
    {
        using var fixture = new ReparsePointFixture();
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "anything" });

        var text = Assert.IsType<string>(result);
        Assert.Equal("No matches.", text);
    }

    /// <summary>
    ///     Proves a case-insensitive search matches regardless of case.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_IgnoreCase_MatchesRegardlessOfCase()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "Hello World\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "hello", ["ignoreCase"] = true });

        var text = Assert.IsType<string>(result);
        Assert.Contains("note.txt:1:Hello World", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file reachable only through a link outside the grants is never surfaced by a
    ///     search — not its content, not its path, not its existence.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_FileBeneathLinkOutsideRoot_IsNeverSurfaced()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it, with a file
        // whose content would match the search
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "TOP-SECRET-TOKEN");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedFile = Path.Combine(link, "secret.txt");
        Assert.Equal("TOP-SECRET-TOKEN", await System.IO.File.ReadAllTextAsync(
            escapedFile, TestContext.Current.CancellationToken));
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        // Act: search for the exact token the escaped file contains
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "TOP-SECRET-TOKEN" });

        // Assert: the search surfaces nothing — not the content, path, or existence of the file
        var text = Assert.IsType<string>(result);
        Assert.Equal("No matches.", text);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves a directory outside the grants is refused rather than searched silently.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_DirectoryOutsideGrants_ReturnsDenial()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "token");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "token", ["path"] = fixture.Outside });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file glob restricts the search to matching files.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_FilePattern_RestrictsToMatchingFiles()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "target\n");
        ReparsePointFixture.WriteFile(fixture.Root, "guide.md", "target\n");
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["pattern"] = "target", ["filePattern"] = "*.md" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("guide.md:1:target", text, StringComparison.Ordinal);
        Assert.DoesNotContain("note.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a match inside a file larger than the read ceiling is reported rather than the file
    ///     being silently skipped for size. This is a regression test for the windowing defect: an
    ///     oversized text file was invisible to search, and must now be streamed and searched.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_MatchInsideLargeFile_IsReported()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "big.txt", LargeFileWithTokenOnLine(300, 250, "beacon"));
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 64)));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "beacon" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("big.txt:250:beacon", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a binary file is skipped rather than searched or disclosed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileSearchTool_Search_BinaryFile_IsSkipped()
    {
        using var fixture = new ReparsePointFixture();
        // A file with a NUL byte and text that would otherwise match.
        ReparsePointFixture.WriteBytes(
            fixture.Root, "data.bin", [0x74, 0x6F, 0x6B, 0x65, 0x6E, 0x00, 0x74, 0x6F, 0x6B, 0x65, 0x6E]);
        var tool = TextFileSearchTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["pattern"] = "token" });

        var text = Assert.IsType<string>(result);
        Assert.Equal("No matches.", text);
    }

    /// <summary>
    ///     Builds a multi-line fixture whose bytes exceed a small read ceiling while each line stays
    ///     tiny, carrying a unique token on one line, so streaming search finds a match that a
    ///     whole-file size gate would have skipped.
    /// </summary>
    /// <param name="lineCount">The number of lines to generate.</param>
    /// <param name="tokenLine">The 1-based line the token is placed on.</param>
    /// <param name="token">The unique token to place.</param>
    /// <returns>The fixture text, with a trailing newline.</returns>
    private static string LargeFileWithTokenOnLine(int lineCount, int tokenLine, string token)
    {
        var builder = new StringBuilder();
        for (var number = 1; number <= lineCount; number++)
        {
            builder.Append(number == tokenLine ? token : "filler line").Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Composes a policy over one read-write location, optionally with tighter limits.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <param name="limits">The tool limits, or null for the defaults.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return limits is null
            ? new PathPolicy(root, [PathRule.ReadWrite(root)])
            : new PathPolicy(root, [PathRule.ReadWrite(root)], limits);
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
