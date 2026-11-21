using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Memory;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests;

/// <summary>
///     System-level integration tests for the AgentKitTools system.
/// </summary>
public class AgentKitToolsTests
{
    /// <summary>
    ///     Proves that the package composes its tool families through the AgentKitCore contract,
    ///     and that an empty composition — before any family is attached — yields no tools.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_EmptyBuilder_ContributesNoTools()
    {
        // Arrange: a policy governing an otherwise empty composition
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: build the tool list before any family has been attached
        var tools = new ToolPackBuilder(policy).Build();

        // Assert: an empty composition contributes no tools
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that attaching the TextFile pack contributes the text file family to a
    ///     composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_TextFilePack_ContributesTheTextFileFamily()
    {
        // Arrange: a policy governing a composition with the text file family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new TextFilePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's eight tools are published, each under the family prefix. The policy
        // permits writing, which is what publishes the five write-performing tools.
        Assert.Equal(
            [
                "text_file_search",
                "text_file_read",
                "text_file_create",
                "text_file_write",
                "text_file_replace",
                "text_file_cut_lines",
                "text_file_copy_lines",
                "text_file_paste_lines"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the File pack contributes the type-agnostic file family to a
    ///     composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_FilePack_ContributesTheFileFamily()
    {
        // Arrange: a policy governing a composition with the file family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new FilePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's seven tools are published, each under the family prefix. The policy
        // permits writing, which is what publishes the six that change the file system.
        Assert.Equal(
            [
                "file_list",
                "file_copy",
                "file_move",
                "file_delete",
                "file_create_directory",
                "file_move_directory",
                "file_delete_directory"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Markdown pack contributes the markdown family to a composition,
    ///     under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_MarkdownPack_ContributesTheMarkdownFamily()
    {
        // Arrange: a policy governing a composition with the markdown family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new MarkdownPack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's single tool is published, under the family prefix
        Assert.Equal(
            ["markdown_outline"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Image pack to a vision host contributes the image family to
    ///     a composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_ImagePack_ContributesTheImageFamily()
    {
        // Arrange: a vision host governing a composition with the image family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: every one of the family's tools is published, under the family prefix
        Assert.Equal(
            ["image_read", "image_crop", "image_auto_crop"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a host that does not declare vision receives none of the image family's
    ///     tools, so a model that cannot see is never offered content it could only fabricate
    ///     around.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_ImagePackWithoutVision_ContributesNoTools()
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
    ///     Proves that attaching the Memory pack contributes the memory family to a composition,
    ///     under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_MemoryPack_ContributesTheMemoryFamily()
    {
        // Arrange: a policy governing a composition with the memory family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .Add(new MemoryPack(new Memory.StubEmbeddingGenerator()));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's five tools are published, each under the family prefix
        Assert.Equal(
            ["memory_file", "memory_recall", "memory_update", "memory_revise", "memory_forget"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Todo pack contributes the todo family to a composition, under
    ///     the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_TodoPack_ContributesTheTodoFamily()
    {
        // Arrange: a policy governing a composition with the todo family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new TodoPack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's three tools are published, each under the family prefix
        Assert.Equal(
            ["todo_list", "todo_set", "todo_remove"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Agent pack to a delegating host contributes the agent family to
    ///     a composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_AgentPack_ContributesTheAgentFamily()
    {
        // Arrange: a delegating host governing a composition with the agent family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([], (_, _) => Task.FromResult<string?>("done"), []));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's run tool is published, under the family prefix
        Assert.Equal(
            ["agent_run"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a host that does not declare delegation receives none of the agent family's
    ///     tools, so an application that cannot start a second agent never offers one.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_AgentPackWithoutDelegation_ContributesNoTools()
    {
        // Arrange: a host that declares no capability, governing a composition with the agent
        // family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .Add(new AgentPack([], (_, _) => Task.FromResult<string?>("done"), []));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the delegation requirement is unmet, so the family contributes nothing
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that all seven families composed over one policy that permits no writing anywhere
    ///     withhold exactly the tools that can act only by writing, so an agent is never
    ///     offered a tool whose only possible outcome would be a refusal because nothing is
    ///     writable.
    /// </summary>
    /// <remarks>
    ///     This is the system-level statement of the rule: the pack declares what tools exist, the
    ///     policy decides which writes are possible, and the tools that can only write are
    ///     withheld. The exact
    ///     ordered list is asserted rather than set membership, because both which tools survive and
    ///     the order a model sees them in are observable and are part of the contract. The grant is
    ///     read-only rather than absent on purpose: under a read-only grant every surviving tool
    ///     can genuinely succeed, which is what makes the published set a truthful statement here.
    /// </remarks>
    [Fact]
    public void AgentKitTools_SystemComposition_ReadOnlyPolicy_PublishesOnlyTheToolsThePolicyCanPermit()
    {
        // Arrange: every grant is read-only, and a host granting every capability so that nothing
        // is withheld by the capability gate and the policy is the only filter in play
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision | HostCapabilities.Delegation)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new MarkdownPack())
            .Add(new ImagePack())
            .Add(new MemoryPack(new Memory.StubEmbeddingGenerator()))
            .Add(new TodoPack())
            .Add(new AgentPack([], (_, _) => Task.FromResult<string?>("done"), []));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: 3 text-file + 1 file + 1 markdown + 3 image + 5 memory + 3 todo + 1 agent = 17,
        // in pack-add order. The eleven write-performing tools are absent.
        Assert.Equal(17, tools.Count);
        Assert.Equal(
            [
                "text_file_search",
                "text_file_read",
                "text_file_copy_lines",
                "file_list",
                "markdown_outline",
                "image_read",
                "image_crop",
                "image_auto_crop",
                "memory_file",
                "memory_recall",
                "memory_update",
                "memory_revise",
                "memory_forget",
                "todo_list",
                "todo_set",
                "todo_remove",
                "agent_run"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that the same seven-family composition over a policy granting a read-only
    ///     workspace and a writable session location publishes every tool, because the write
    ///     question is asked of the policy as a whole rather than of the location relative names
    ///     anchor to.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool()
    {
        // Arrange: the mixed shape — read the user's documents, write artifacts somewhere else
        using var fixture = new TextFile.TempDirectoryFixture();
        var session = Path.Combine(fixture.Outside, "session");
        Directory.CreateDirectory(session);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(session)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision | HostCapabilities.Delegation)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new MarkdownPack())
            .Add(new ImagePack())
            .Add(new MemoryPack(new Memory.StubEmbeddingGenerator()))
            .Add(new TodoPack())
            .Add(new AgentPack([], (_, _) => Task.FromResult<string?>("done"), []));

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: 8 text-file + 7 file + 1 markdown + 3 image + 5 memory + 3 todo + 1 agent = 28,
        // in pack-add order
        Assert.Equal(28, tools.Count);
        Assert.Equal(
            [
                "text_file_search",
                "text_file_read",
                "text_file_create",
                "text_file_write",
                "text_file_replace",
                "text_file_cut_lines",
                "text_file_copy_lines",
                "text_file_paste_lines",
                "file_list",
                "file_copy",
                "file_move",
                "file_delete",
                "file_create_directory",
                "file_move_directory",
                "file_delete_directory",
                "markdown_outline",
                "image_read",
                "image_crop",
                "image_auto_crop",
                "memory_file",
                "memory_recall",
                "memory_update",
                "memory_revise",
                "memory_forget",
                "todo_list",
                "todo_set",
                "todo_remove",
                "agent_run"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a child whose profile narrows the policy to a read-only grant receives a
    ///     policy-filtered tool set, and that a write tool its allow-list names is simply absent
    ///     rather than an error.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is emergent behavior worth asserting rather than leaving to be discovered. The
    ///     agent pack composes a child through a fresh builder over the child's own policy, so
    ///     policy-derived suppression applies at every depth with no agent-pack change. The profile
    ///     then intersects its allow-list with what that composition published — the profile is a
    ///     filter over what was published, never a source of tools — so naming a tool the child's
    ///     policy suppressed yields absence, not a failure.
    ///     </para>
    ///     <para>
    ///     The profile below deliberately names <c>text_file_write</c>, a tool that would be
    ///     published to the read-write parent and is withheld from the read-only child. A test whose
    ///     profile named only read tools would pass whether or not suppression reached the child.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitTools_SystemComposition_DelegatedAgentWithReadOnlyProfile_ReceivesOnlyReadTools()
    {
        // Arrange: a read-write parent, and a profile narrowing the same root to read-only whose
        // allow-list names one write tool alongside three tools the child can genuinely use
        using var fixture = new TextFile.TempDirectoryFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);
        var profile = new AgentProfile(
            "reader",
            "You read and report, and never change anything.",
            ["text_file_search", "text_file_read", "text_file_write", "file_list"],
            [PathRule.ReadOnly(fixture.Root)]);

        // The runner stands in for a real child agent, capturing what it was handed.
        IReadOnlyList<AIFunction> childTools = [];
        Task<string?> Runner(ChildAgentRequest request, CancellationToken token)
        {
            childTools = request.Tools;
            return Task.FromResult<string?>("read and reported");
        }

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new AgentPack([profile], Runner, [new TextFilePack(), new FilePack()]))
            .Build();

        // Act: confirm the parent holds the write tool, then delegate to the read-only child
        var parentHasWrite = tools.Any(tool => tool.Name == "text_file_write");
        await tools.Single(tool => tool.Name == "agent_run").InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reader", ["task"] = "Report what you find." },
            TestContext.Current.CancellationToken);

        // Assert: the parent could write; the child received only the three named tools its own
        // read-only policy published, with the named write tool simply absent
        Assert.True(parentHasWrite);
        Assert.Equal(
            ["text_file_search", "text_file_read", "file_list"],
            childTools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a delegated agent's task list is its own: what a child writes never appears
    ///     in its parent's list, and what the parent wrote never appears in the child's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the regression test for a real class of defect. Two separate ways of composing a
    ///     child were tried while the families were being spiked, and both routed a sub-agent's task
    ///     list straight into its parent's: capturing the parent's store while building the child's
    ///     tools, and filtering a parent-bound tool list at the <c>agent_run</c> call site. The
    ///     observable symptom in the field is a sub-agent's short checklist replacing a parent's
    ///     long one.
    ///     </para>
    ///     <para>
    ///     Both stores are asserted, not just the parent's. A test that checked only the parent
    ///     would pass against a composition that gave the child no list at all, and a test that
    ///     checked only the child would pass against one where both wrote into the parent's.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitTools_SystemComposition_DelegatedAgent_KeepsItsOwnTaskList()
    {
        // Arrange: a parent carrying the todo family and the agent family, and a child profile
        // carrying the todo family too
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var profile = new AgentProfile(
            "worker",
            "You do one job and track it.",
            ["todo_list", "todo_set"]);

        // The runner stands in for a real child agent: it writes two steps into whatever list it
        // was given, and reports what that list then holds.
        JsonElement childList = default;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = async (request, token) =>
        {
            var set = request.Tools.Single(tool => tool.Name == "todo_set");
            await set.InvokeAsync(
                new AIFunctionArguments { ["id"] = "child-a", ["title"] = "Child step A" },
                token);
            await set.InvokeAsync(
                new AIFunctionArguments { ["id"] = "child-b", ["title"] = "Child step B" },
                token);

            var list = request.Tools.Single(tool => tool.Name == "todo_list");
            childList = Assert.IsType<JsonElement>(
                await list.InvokeAsync(new AIFunctionArguments(), token));

            return "recorded two steps";
        };

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new TodoPack())
            .Add(new AgentPack([profile], runner, [new TodoPack()]))
            .Build();

        var parentSet = tools.Single(tool => tool.Name == "todo_set");
        var parentList = tools.Single(tool => tool.Name == "todo_list");

        // Act: the parent records one step of its own, then delegates
        await parentSet.InvokeAsync(
            new AIFunctionArguments { ["id"] = "parent-only", ["title"] = "The parent's own step" },
            TestContext.Current.CancellationToken);

        await tools.Single(tool => tool.Name == "agent_run").InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do your job." },
            TestContext.Current.CancellationToken);

        var parentResult = Assert.IsType<JsonElement>(
            await parentList.InvokeAsync(
                new AIFunctionArguments(),
                TestContext.Current.CancellationToken));

        // Assert: the parent's list holds exactly its own step, and the child's holds exactly its
        // own two. Either of the two contamination defects would put three items in the parent's.
        Assert.Equal(1, parentResult.GetProperty("itemCount").GetInt32());
        Assert.Equal(
            ["parent-only"],
            parentResult.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()));

        Assert.Equal(2, childList.GetProperty("itemCount").GetInt32());
        Assert.Equal(
            ["child-a", "child-b"],
            childList.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()));
    }

    /// <summary>
    ///     Proves that a composition retaining large outputs retains the built-in families'
    ///     oversized results for paging, while <c>text_file_read</c> - whose source is already
    ///     pageable - keeps refusing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitTools_SystemComposition_WithToolOutputSpill_OversizedBuiltInResultsAreRetained()
    {
        // Arrange: a workspace of many files, the smallest result ceiling retention accepts, and
        // three families composed with retention
        using var fixture = new TextFile.TempDirectoryFixture();
        for (var index = 1; index <= 200; index++)
        {
            TextFile.TempDirectoryFixture.WriteFile(fixture.Root, "note-" + index + ".md", "# Note " + index + "\nhit\n");
        }

        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root)],
            new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters));
        var tools = new ToolPackBuilder(policy)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new MarkdownPack())
            .WithToolOutputSpill()
            .Build();
        TextFile.TempDirectoryFixture.WriteFile(fixture.Root, "long.txt", string.Concat(Enumerable.Repeat("a line of text\n", 300)));

        // Act: list, search, and read a long file; then search the retained search result
        var listing = await Invoke(tools, FileListTool.ToolName, new() { ["directory"] = fixture.Root });
        var search = await Invoke(tools, TextFileSearchTool.ToolName, new() { ["pattern"] = "hit" });
        var read = await Invoke(tools, TextFileReadTool.ToolName, new() { ["path"] = "long.txt" });
        var found = await Invoke(tools, ToolOutputSearchTool.ToolName, new() { ["id"] = "output-2", ["pattern"] = "note-200.md" });

        // Assert: listing and search are retained, the read keeps refusing, and the retained
        // search holds the last file's match
        Assert.StartsWith("[Output output-1 from " + FileListTool.ToolName, listing, StringComparison.Ordinal);
        Assert.StartsWith("[Output output-2 from " + TextFileSearchTool.ToolName, search, StringComparison.Ordinal);
        Assert.StartsWith("Denied (ResourceTooLarge)", read, StringComparison.Ordinal);
        Assert.Contains("note-200.md:2:hit", found, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a delegated agent's answer too large for the parent's ceiling is retained by
    ///     a parent composed with retention, and that a child composed with retention is handed the
    ///     reader tools.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitTools_SystemComposition_DelegatedAgentOversizedAnswer_IsRetainedByTheParent()
    {
        // Arrange: a parent retaining large outputs, and children composed with retention too
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters));
        var profile = new AgentProfile("worker", "You work.", ["todo_list"]);
        IReadOnlyList<AIFunction>? childTools = null;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            childTools = request.Tools;
            return Task.FromResult<string?>(string.Join("\n", Enumerable.Range(1, 300).Select(n => "finding " + n)));
        };
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, [new TodoPack()], HostCapabilities.Delegation, childToolOutput: ToolOutputOptions.Default))
            .WithToolOutputSpill()
            .Build();

        // Act: delegate, then read the end of the child's answer
        var answer = await Invoke(tools, AgentRunTool.ToolName, new() { ["profile"] = "worker", ["task"] = "Report." });
        var page = await Invoke(tools, ToolOutputReadTool.ToolName, new() { ["id"] = "output-1", ["startLine"] = 300, ["lineCount"] = 1 });

        // Assert: the parent retained the whole answer, and the child had readers of its own
        Assert.StartsWith("[Output output-1 from " + AgentRunTool.ToolName, answer, StringComparison.Ordinal);
        Assert.Equal("output-1 lines 300-300 of 300\n300| finding 300", page);
        Assert.NotNull(childTools);
        Assert.Equal(
            ["todo_list", ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName],
            childTools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a path tool's denial discloses no host absolute path: the refusal from every
    ///     path-taking family names the permitted location relative to the working directory, and
    ///     neither the working directory nor the temporary base it sits in appears.
    /// </summary>
    /// <remarks>
    ///     Every path-taking tool passes the access policy's denial through unchanged, so one
    ///     scenario across the families is the system-level evidence that a refusal never adds a
    ///     host location the model was not already given.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitTools_PathDenials_DiscloseNoHostAbsolutePath()
    {
        // Arrange: a read-only workspace with every path-taking family that reads a single path
        using var fixture = new TextFile.TempDirectoryFixture();
        TextFile.TempDirectoryFixture.WriteFile(fixture.Outside, "x.png", "not really an image");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new ImagePack())
            .Build();

        // Act: each tool is asked for a location outside the grant, the way a model escapes
        var denials = new[]
        {
            await Invoke(tools, TextFileReadTool.ToolName, new() { ["path"] = "../outside/x.png" }),
            await Invoke(tools, TextFileSearchTool.ToolName, new() { ["pattern"] = "x", ["path"] = "../outside" }),
            await Invoke(tools, FileListTool.ToolName, new() { ["directory"] = "../outside" }),
            await Invoke(tools, ImageReadTool.ToolName, new() { ["path"] = "../outside/x.png" })
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
