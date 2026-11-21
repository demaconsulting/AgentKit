using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Unit tests for the <see cref="AgentPack"/> class.
/// </summary>
public class AgentPackTests
{
    /// <summary>
    ///     A runner that reports a fixed answer, for scenarios where the child's answer is not the
    ///     subject.
    /// </summary>
    private static readonly Func<ChildAgentRequest, CancellationToken, Task<string?>> FixedAnswer = (_, _) => Task.FromResult<string?>("done");

    /// <summary>
    ///     Proves the pack publishes its family prefix as a constant and through the contract.
    /// </summary>
    [Fact]
    public void AgentPack_FamilyPrefix_IsPublishedAsConstantAndContract()
    {
        var pack = new AgentPack([], FixedAnswer, []);

        Assert.Equal("agent", AgentPack.FamilyPrefix);
        Assert.Equal(AgentPack.FamilyPrefix, ((IToolPack)pack).FamilyPrefix);
    }

    /// <summary>
    ///     Proves the family is gated on the host's declaration that it can start a further agent,
    ///     exactly as the image family is gated on vision.
    /// </summary>
    [Fact]
    public void AgentPack_RequiredCapabilities_IsDelegation()
    {
        Assert.Equal(HostCapabilities.Delegation, new AgentPack([], FixedAnswer, []).RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the pack creates its single delegation tool.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_RegistersTheRunTool()
    {
        // Arrange / Act: compose the family
        var tools = new AgentPack([], FixedAnswer, []).CreateTools(Policy()).ToList();

        // Assert: one tool, carrying the family prefix
        Assert.Equal([AgentRunTool.ToolName], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a read-only path policy still publishes the delegation tool, because starting a
    ///     child agent writes no file — the policy's role here is to bound what a child may be
    ///     granted, not to permit an action this tool performs.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ReadOnlyPolicy_StillPublishesTheRunTool()
    {
        // Arrange: every grant is read-only, so nothing anywhere may be written
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: compose the family under that policy
        var tools = new AgentPack([], FixedAnswer, []).CreateTools(policy).ToList();

        // Assert: the delegation tool survives
        Assert.Equal([AgentRunTool.ToolName], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack requires a policy to create its tools, since the policy is what a child's
    ///     grants are judged against.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new AgentPack([], FixedAnswer, []).CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves the parts a composing application supplies are mandatory.
    /// </summary>
    [Fact]
    public void AgentPack_Constructor_MissingParts_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentPack(null!, FixedAnswer, []));
        Assert.Throws<ArgumentNullException>(() => new AgentPack([], null!, []));
        Assert.Throws<ArgumentNullException>(() => new AgentPack([], FixedAnswer, null!));
    }

    /// <summary>
    ///     Proves two profiles of one name are refused at construction, since a model selects a
    ///     profile by name and the tool would otherwise silently always choose the first.
    /// </summary>
    [Fact]
    public void AgentPack_Constructor_DuplicateProfileName_ThrowsArgumentException()
    {
        // Arrange: two registrations claiming one name
        var profiles = new[]
        {
            new AgentProfile("reviewer", "Review.", []),
            new AgentProfile("reviewer", "Review differently.", []),
        };

        // Act / Assert: refused at the line that made the mistake
        Assert.Throws<ArgumentException>(() => new AgentPack(profiles, FixedAnswer, []));
    }

    /// <summary>
    ///     Proves a null profile or a null child pack is refused at construction.
    /// </summary>
    [Fact]
    public void AgentPack_Constructor_NullEntries_ThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new AgentPack([null!], FixedAnswer, []));
        Assert.Throws<ArgumentException>(() => new AgentPack([], FixedAnswer, [null!]));
    }

    /// <summary>
    ///     Proves the agent pack cannot be listed among the packs a child draws on, because the
    ///     family adds itself to every child composition one level deeper and listing it too would
    ///     publish the prefix twice.
    /// </summary>
    [Fact]
    public void AgentPack_Constructor_AgentPackAsChildPack_ThrowsArgumentException()
    {
        // Arrange: a pack a careless application might try to pass through to its children
        var inner = new AgentPack([], FixedAnswer, []);

        // Act / Assert: refused, naming why
        var exception = Assert.Throws<ArgumentException>(
            () => new AgentPack([], FixedAnswer, [inner]));
        Assert.Contains(AgentRunTool.ToolName, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves two child packs claiming one family prefix are refused when the application
    ///     registers them, rather than the first time a model happens to delegate.
    /// </summary>
    [Fact]
    public void AgentPack_Constructor_DuplicateChildPackPrefix_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => new AgentPack(
                [],
                FixedAnswer,
                [new StubToolPack("notes", "read"), new StubToolPack("notes", "write")]));
    }

    /// <summary>
    ///     Proves a profile naming a tool the application never attached does not conjure it: the
    ///     child receives only the names that were both declared and published.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ProfileNamingAnUnattachedTool_DoesNotConjureIt()
    {
        // Arrange: a profile asking for tools no attached pack publishes
        var profile = new AgentProfile(
            "worker",
            "You do one job.",
            ["notes_read", "shell_exec", "text_file_read"]);

        ChildAgentRequest? seen = null;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            seen = request;
            return Task.FromResult<string?>("done");
        };

        // Only the notes family is attached, so shell_exec and text_file_read exist nowhere.
        var tools = new AgentPack([profile], runner, [new StubToolPack("notes", "read", "write")])
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: the child got the one tool that really exists, and neither of the other two
        Assert.NotNull(seen);
        Assert.Equal(["notes_read"], seen.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a child's tools are the intersection of the profile's declared names and what the
    ///     application attached, so a profile narrows a child's reach and never widens it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildTools_AreASubsetOfWhatWasAttached()
    {
        // Arrange: a family of three tools attached, a profile admitting two of them
        var profile = new AgentProfile("reviewer", "You review.", ["notes_read", "notes_search"]);

        ChildAgentRequest? seen = null;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            seen = request;
            return Task.FromResult<string?>("done");
        };

        var pack = new StubToolPack("notes", "read", "search", "write");
        var tools = new AgentPack([profile], runner, [pack]).CreateTools(Policy()).ToList();

        // Act: delegate
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Review it." },
            TestContext.Current.CancellationToken);

        // Assert: exactly the two admitted tools, in composition order, and not the write tool
        Assert.NotNull(seen);
        Assert.Equal(["notes_read", "notes_search"], seen.Tools.Select(tool => tool.Name));
        Assert.DoesNotContain(seen.Tools, tool => tool.Name == "notes_write");
    }

    /// <summary>
    ///     Proves a child's tools are created afresh for the child rather than being the parent's
    ///     own tools handed down, which is the property that keeps any per-composition state a
    ///     family holds from being shared between an agent and its children.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildTools_AreComposedAfreshPerRun()
    {
        // Arrange: one registered pack, and a runner that keeps each child's tool instances
        var profile = new AgentProfile("worker", "You work.", ["notes_read"]);

        var handedOut = new List<AIFunction>();
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            handedOut.Add(request.Tools[0]);
            return Task.FromResult<string?>("done");
        };

        var pack = new StubToolPack("notes", "read");
        var parentTools = new AgentPack([profile], runner, [pack]).CreateTools(Policy()).ToList();

        // Act: delegate twice
        for (var run = 0; run < 2; run++)
        {
            await parentTools[0].InvokeAsync(
                new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
                TestContext.Current.CancellationToken);
        }

        // Assert: two separate compositions, neither of them the parent's
        Assert.Equal(2, pack.Policies.Count);
        Assert.NotSame(handedOut[0], handedOut[1]);
    }

    /// <summary>
    ///     Proves a profile whose grants reach a location the composition cannot reach is refused at
    ///     composition, so delegation is never a route by which an agent acquires reach its operator
    ///     did not give it.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ProfileGrantingAnUnreachableLocation_Throws()
    {
        // Arrange: a parent confined to one directory, and a profile pointing somewhere else
        using var workspace = new TemporaryDirectory();
        using var elsewhere = new TemporaryDirectory();

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var profile = new AgentProfile(
            "wanderer",
            "You work elsewhere.",
            [],
            grants: [PathRule.ReadOnly(elsewhere.Path)]);

        // Act / Assert: the configuration error is reported to the developer who wrote it
        var exception = Assert.Throws<InvalidOperationException>(
            () => new AgentPack([profile], FixedAnswer, []).CreateTools(policy).ToList());
        Assert.Contains("may only narrow", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a profile asking to write where its parent may only read is refused, since an
    ///     access level is part of a grant's width.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ProfileRaisingTheAccessLevel_Throws()
    {
        // Arrange: a read-only parent and a profile wanting to write the same location
        using var workspace = new TemporaryDirectory();

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadOnly(workspace.Path)]);
        var profile = new AgentProfile(
            "editor",
            "You edit.",
            [],
            grants: [PathRule.ReadWrite(workspace.Path)]);

        // Act / Assert: a write is wider than a read over the same ground
        Assert.Throws<InvalidOperationException>(
            () => new AgentPack([profile], FixedAnswer, []).CreateTools(policy).ToList());
    }

    /// <summary>
    ///     Proves a profile asking for unrestricted reach is refused when its parent is confined,
    ///     since no rooted grant can contain "everywhere".
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ProfileGrantingUnrestrictedReach_Throws()
    {
        // Arrange: a confined parent and a profile wanting the whole machine
        using var workspace = new TemporaryDirectory();

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var profile = new AgentProfile(
            "wanderer",
            "You roam.",
            [],
            grants: [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act / Assert: refused
        Assert.Throws<InvalidOperationException>(
            () => new AgentPack([profile], FixedAnswer, []).CreateTools(policy).ToList());
    }

    /// <summary>
    ///     Proves a profile that drops an exclusion its parent imposes is refused, because a grant
    ///     over the same root with the parent's exclusions removed is strictly wider while wearing a
    ///     narrower shape.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ProfileDroppingAParentExclusion_Throws()
    {
        // Arrange: a parent that excludes a directory, and a profile that does not
        using var workspace = new TemporaryDirectory();

        var policy = new PathPolicy(
            workspace.Path,
            [PathRule.ReadWrite(workspace.Path, [".git"])]);
        var profile = new AgentProfile(
            "worker",
            "You work.",
            [],
            grants: [PathRule.ReadWrite(workspace.Path)]);

        // Act / Assert: the missing exclusion is a widening
        Assert.Throws<InvalidOperationException>(
            () => new AgentPack([profile], FixedAnswer, []).CreateTools(policy).ToList());
    }

    /// <summary>
    ///     Proves a genuinely narrower profile is accepted and that the child is composed against
    ///     the narrowed policy, so narrowing remains expressible and actually reaches the child.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ProfileNarrowingTheGrants_IsAcceptedAndReachesTheChild()
    {
        // Arrange: a parent over a workspace, and a profile confined to a directory inside it and
        // downgraded to read-only
        using var workspace = new TemporaryDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(workspace.Path, "notes")).FullName;

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var profile = new AgentProfile("reader", "You read notes.", ["notes_read"], [PathRule.ReadOnly(inner)]);

        var pack = new StubToolPack("notes", "read");
        var tools = new AgentPack([profile], FixedAnswer, [pack]).CreateTools(policy).ToList();

        // Act: compose a child
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reader", ["task"] = "Read it." },
            TestContext.Current.CancellationToken);

        // Assert: accepted, and the child's tools were built against the narrowed policy
        Assert.Single(tools);
        var childPolicy = Assert.Single(pack.Policies);
        Assert.NotSame(policy, childPolicy);
        Assert.Equal(AccessLevel.ReadOnly, Assert.Single(childPolicy.Grants).Access);
        Assert.Equal(policy.WorkingDirectory, childPolicy.WorkingDirectory);
    }

    /// <summary>
    ///     Proves a profile that states no grants leaves the child observing its parent's policy
    ///     unchanged, which is the common case.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ProfileWithoutGrants_InheritsTheParentPolicy()
    {
        // Arrange: a parent policy and a profile that narrows nothing
        var policy = Policy();
        var profile = new AgentProfile("worker", "You work.", ["notes_read"]);
        var pack = new StubToolPack("notes", "read");
        var tools = new AgentPack([profile], FixedAnswer, [pack]).CreateTools(policy).ToList();

        // Act: compose a child
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: the child was composed against the parent's own policy
        Assert.Same(policy, Assert.Single(pack.Policies));
    }

    /// <summary>
    ///     Proves delegating to the narrowest of several registered profiles succeeds even though a
    ///     sibling profile states grants the child's own policy no longer covers, and that the child
    ///     cannot reach the sibling's wider location. A sibling a child cannot reach is not a
    ///     configuration error; it is simply a profile that child cannot delegate to.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_DelegatingToTheNarrowerOfTwoProfiles_Succeeds()
    {
        // Arrange: a parent holding the whole workspace read-write, a reviewer confined read-only to
        // a directory inside it, and an editor stating the parent's own full grant
        using var workspace = new TemporaryDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(workspace.Path, "notes")).FullName;

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var reviewer = new AgentProfile("reviewer", "You review notes.", ["notes_read"], [PathRule.ReadOnly(inner)]);
        var editor = new AgentProfile("editor", "You edit the workspace.", ["notes_write"], [PathRule.ReadWrite(workspace.Path)]);

        var pack = new StubToolPack("notes", "read", "write");
        var tools = new AgentPack([reviewer, editor], FixedAnswer, [pack]).CreateTools(policy).ToList();

        // Act: delegate to the narrower profile
        var result = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Read the notes." },
            TestContext.Current.CancellationToken);

        // Assert: the run completed rather than throwing over the sibling's wider grant
        Assert.NotNull(result);

        // Assert: the child was composed against the narrowed policy and cannot reach the editor's
        // location outside it
        var childPolicy = Assert.Single(pack.Policies);
        Assert.Equal(AccessLevel.ReadOnly, Assert.Single(childPolicy.Grants).Access);
        Assert.True(childPolicy.TryResolveRead(Path.Combine(inner, "note.txt"), out _, out _));
        Assert.False(childPolicy.TryResolveRead(Path.Combine(workspace.Path, "outside.txt"), out _, out _));
    }

    /// <summary>
    ///     Proves a child whose profile admits delegation can compose a grandchild without the same
    ///     sibling-profile failure reappearing one level down.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_GrandchildDelegation_Succeeds()
    {
        // Arrange: a reviewer that may itself delegate, alongside a wider editor sibling
        using var workspace = new TemporaryDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(workspace.Path, "notes")).FullName;

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var reviewer = new AgentProfile(
            "reviewer",
            "You review notes and may delegate.",
            ["notes_read", AgentRunTool.ToolName],
            [PathRule.ReadOnly(inner)]);
        var editor = new AgentProfile("editor", "You edit the workspace.", ["notes_write"], [PathRule.ReadWrite(workspace.Path)]);

        var children = new List<ChildAgentRequest>();
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            children.Add(request);
            return Task.FromResult<string?>("done");
        };

        var pack = new StubToolPack("notes", "read", "write");
        var tools = new AgentPack([reviewer, editor], runner, [pack]).CreateTools(policy).ToList();

        // Act: delegate, then have the child delegate in turn
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Read the notes." },
            TestContext.Current.CancellationToken);

        var childRunTool = Assert.Single(children[0].Tools, tool => tool.Name == AgentRunTool.ToolName);
        var result = await childRunTool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Read them again." },
            TestContext.Current.CancellationToken);

        // Assert: a grandchild was composed at the greater depth, on the same narrowed policy
        Assert.NotNull(result);
        Assert.Equal(2, children.Count);
        Assert.Equal(2, children[1].Depth);
        Assert.Equal(2, pack.Policies.Count);
        Assert.False(pack.Policies[1].TryResolveRead(Path.Combine(workspace.Path, "outside.txt"), out _, out _));
    }

    /// <summary>
    ///     Proves a profile the child's own policy no longer covers is simply absent from the child,
    ///     so a model naming it receives the ordinary unknown-profile refusal rather than reaching a
    ///     location its parent narrowed away.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildNamingAnUnreachableSiblingProfile_IsRefused()
    {
        // Arrange: as above, a narrow reviewer that may delegate and a wider editor
        using var workspace = new TemporaryDirectory();
        var inner = Directory.CreateDirectory(Path.Combine(workspace.Path, "notes")).FullName;

        var policy = new PathPolicy(workspace.Path, [PathRule.ReadWrite(workspace.Path)]);
        var reviewer = new AgentProfile(
            "reviewer",
            "You review notes and may delegate.",
            ["notes_read", AgentRunTool.ToolName],
            [PathRule.ReadOnly(inner)]);
        var editor = new AgentProfile("editor", "You edit the workspace.", ["notes_write"], [PathRule.ReadWrite(workspace.Path)]);

        ChildAgentRequest? child = null;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            child ??= request;
            return Task.FromResult<string?>("done");
        };

        var tools = new AgentPack([reviewer, editor], runner, [new StubToolPack("notes", "read", "write")])
            .CreateTools(policy)
            .ToList();

        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Read the notes." },
            TestContext.Current.CancellationToken);

        // Act: the child names the sibling its own policy cannot cover
        Assert.NotNull(child);
        var childRunTool = Assert.Single(child.Tools, tool => tool.Name == AgentRunTool.ToolName);
        var result = await childRunTool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "editor", ["task"] = "Edit the workspace." },
            TestContext.Current.CancellationToken);

        // Assert: the ordinary unknown-profile refusal, stating which profiles exist
        var text = Assert.IsType<string>(result);
        Assert.Contains("No profile is named 'editor'", text, StringComparison.Ordinal);
        Assert.Contains("'reviewer'", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a child composed with retention receives the two reader tools after the tools its
    ///     profile admits, even though the profile does not name them.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutput_ChildReceivesTheReadersAfterItsTools()
    {
        // Arrange: a profile admitting one tool, and children composed with retention
        var profile = new AgentProfile("reviewer", "You review.", ["notes_read"]);
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [profile],
                Recording(children),
                [new StubToolPack("notes", "read", "write")],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        await Delegate(tools[0], "reviewer");

        // Assert: the admitted tool, then the readers, and still not the write tool
        Assert.Equal(
            ["notes_read", ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName],
            children[0].Tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a profile that admits no tools stays tool-less under retention, because a child
    ///     with nothing that produces output has nothing to read back.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutputForAToollessProfile_ChildStaysToolless()
    {
        // Arrange: a profile admitting nothing, with retention chosen for children
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [new AgentProfile("summarizer", "You summarize.", [])],
                Recording(children),
                [new StubToolPack("notes", "read")],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        await Delegate(tools[0], "summarizer");

        // Assert: no tools at all
        Assert.Empty(children[0].Tools);
    }

    /// <summary>
    ///     Proves the retention choice is carried to a grandchild, so every level of delegation
    ///     retains.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutput_IsCarriedToAGrandchild()
    {
        // Arrange: a profile that may delegate to itself, with retention chosen for children
        var profile = new AgentProfile("worker", "You work.", ["notes_read", AgentRunTool.ToolName]);
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [profile],
                Recording(children),
                [new StubToolPack("notes", "read")],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate, then have the child delegate in turn
        await Delegate(tools[0], "worker");
        await Delegate(children[0].Tools.Single(tool => tool.Name == AgentRunTool.ToolName), "worker");

        // Assert: the grandchild has the readers too
        Assert.Equal(2, children.Count);
        Assert.Contains(children[1].Tools, tool => tool.Name == ToolOutputReadTool.ToolName);
    }

    /// <summary>
    ///     Proves two children never share retained outputs: each delegated run is its own
    ///     composition with its own store.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutput_EachChildHasItsOwnStore()
    {
        // Arrange: the smallest ceiling retention accepts, and a stub output exceeding it, so each
        // call is retained
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters));
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [new AgentProfile("worker", "You work.", ["notes_read"])],
                Recording(children),
                [new StubToolPack("notes", "read") { Output = new string('x', 2 * ToolOutputSpill.MinimumResultCharacters) }],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(policy)
            .ToList();
        await Delegate(tools[0], "worker");
        await Delegate(tools[0], "worker");

        // Act: the first child retains an output; the second child reads that reference
        var retained = await children[0].Tools[0].InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        var read = await children[1].Tools.Single(tool => tool.Name == ToolOutputReadTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "output-1" },
            TestContext.Current.CancellationToken);

        // Assert: the first child holds a reference, and the second child's store never issued it
        Assert.StartsWith("[Output output-1 from notes_read", Assert.IsType<string>(retained), StringComparison.Ordinal);
        Assert.Contains("never retained", Assert.IsType<string>(read), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that retention for children is rejected at composition when the policy's result
    ///     ceiling, which every child inherits, is below the retention minimum, and that the same
    ///     pack without retention for children composes as it always did.
    /// </summary>
    [Fact]
    public void AgentPack_CreateTools_ChildToolOutputWithResultCeilingBelowMinimum_ThrowsInvalidOperationException()
    {
        // Arrange: a tiny ceiling, and the same registration with and without retention
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxResultCharacters: 2));
        var profile = new AgentProfile("worker", "You work.", ["notes_read"]);
        var retaining = new AgentPack(
            [profile],
            Recording([]),
            [new StubToolPack("notes", "read")],
            HostCapabilities.Delegation,
            childToolOutput: ToolOutputOptions.Default);
        var plain = new AgentPack([profile], Recording([]), [new StubToolPack("notes", "read")]);

        // Act: compose both against the tiny ceiling
        var error = Assert.Throws<InvalidOperationException>(() => retaining.CreateTools(policy));
        var tools = plain.CreateTools(policy).ToList();

        // Assert: the retaining pack names the minimum and the ceiling; the plain pack composes
        Assert.Contains("at least 1000 characters", error.Message, StringComparison.Ordinal);
        Assert.Contains("is 2 characters", error.Message, StringComparison.Ordinal);
        Assert.Equal([AgentRunTool.ToolName], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a retaining child admitted a hand-built tool whose name is longer than any provider
    ///     accepts is refused by <c>agent_run</c> as a composition failure, rather than ending the
    ///     agent's turn with an exception, and the runner is never started.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutputWithOverlongToolName_IsRefused()
    {
        // Arrange: a hand-built tool one character over the limit, admitted by the profile
        var verb = new string('a', ToolName.MaxLength - 5);
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [new AgentProfile("worker", "You work.", ["notes_" + verb])],
                Recording(children),
                [new StubToolPack("notes", verb) { HandBuilt = true }],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        var result = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming the composition failure, and no child was started
        var text = Assert.IsType<string>(result);
        Assert.Contains("could not be composed", text, StringComparison.Ordinal);
        Assert.Contains("longer than 64 characters", text, StringComparison.Ordinal);
        Assert.Empty(children);
    }

    /// <summary>
    ///     Proves that a retaining child admitted a tool under the reserved reader prefix is refused
    ///     by <c>agent_run</c> as a composition failure, rather than ending the agent's turn with an
    ///     exception, and the runner is never started.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutputWithReservedToolName_IsRefused()
    {
        // Arrange: a child pack publishing under the reserved prefix, admitted by the profile
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [new AgentProfile("worker", "You work.", ["tool_output_read"])],
                Recording(children),
                [new StubToolPack("tool_output", "read")],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        var result = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming the reserved tool, and no child was started
        var text = Assert.IsType<string>(result);
        Assert.Contains("could not be composed", text, StringComparison.Ordinal);
        Assert.Contains("reserved", text, StringComparison.Ordinal);
        Assert.Contains("tool_output_read", text, StringComparison.Ordinal);
        Assert.Empty(children);
    }

    /// <summary>
    ///     Proves that a retaining child admitted one name published by two child packs with
    ///     overlapping prefixes is refused by <c>agent_run</c> as a composition failure, rather than
    ///     ending the agent's turn with an exception, and the runner is never started.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_CreateTools_ChildToolOutputWithDuplicateToolName_IsRefused()
    {
        // Arrange: two distinct prefixes that both publish notes_x_read, admitted by the profile
        var children = new List<ChildAgentRequest>();
        var tools = new AgentPack(
                [new AgentProfile("worker", "You work.", ["notes_x_read"])],
                Recording(children),
                [new StubToolPack("notes", "x_read"), new StubToolPack("notes_x", "read")],
                HostCapabilities.Delegation,
                childToolOutput: ToolOutputOptions.Default)
            .CreateTools(Policy())
            .ToList();

        // Act: delegate
        var result = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming the duplicated tool, and no child was started
        var text = Assert.IsType<string>(result);
        Assert.Contains("could not be composed", text, StringComparison.Ordinal);
        Assert.Contains("Two tools carry the name 'notes_x_read'", text, StringComparison.Ordinal);
        Assert.Empty(children);
    }

    /// <summary>
    ///     Proves the constructor that predates retention is still compiled with its exact
    ///     signature, so a host built against an earlier release keeps binding, and that a child it
    ///     composes does not retain large outputs.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentPack_Constructor_WithoutChildToolOutput_KeepsThePreRetentionSignature()
    {
        // Arrange: the four parameter types of the constructor as it was before retention existed
        Type[] signature =
        [
            typeof(IEnumerable<AgentProfile>),
            typeof(Func<ChildAgentRequest, CancellationToken, Task<string>>),
            typeof(IEnumerable<IToolPack>),
            typeof(HostCapabilities),
        ];
        var children = new List<ChildAgentRequest>();

        // Act: look the constructor up, and compose a child through the three-argument call
        var constructor = typeof(AgentPack).GetConstructor(signature);
        var tools = new AgentPack(
                [new AgentProfile("worker", "You work.", ["notes_read"])],
                Recording(children),
                [new StubToolPack("notes", "read")])
            .CreateTools(Policy())
            .ToList();
        await Delegate(tools[0], "worker");

        // Assert: the signature exists, its last parameter keeps its default, and the child has no readers
        Assert.NotNull(constructor);
        Assert.True(constructor.GetParameters()[3].HasDefaultValue);
        Assert.Equal(
            (int)HostCapabilities.Delegation,
            Convert.ToInt32(constructor.GetParameters()[3].DefaultValue, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(["notes_read"], children[0].Tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Builds a runner that records each child request and answers briefly.
    /// </summary>
    /// <param name="children">The list each request is recorded in.</param>
    /// <returns>The runner.</returns>
    private static Func<ChildAgentRequest, CancellationToken, Task<string?>> Recording(List<ChildAgentRequest> children) =>
        (request, _) =>
        {
            children.Add(request);
            return Task.FromResult<string?>("done");
        };

    /// <summary>
    ///     Invokes an <c>agent_run</c> tool for a profile, as the runtime would.
    /// </summary>
    /// <param name="runTool">The <c>agent_run</c> tool.</param>
    /// <param name="profile">The profile to delegate to.</param>
    /// <returns>A task that completes when the delegation has returned.</returns>
    private static async Task Delegate(AIFunction runTool, string profile)
    {
        await runTool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = profile, ["task"] = "Do it." },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Builds a permissive policy for scenarios where containment is not the subject.
    /// </summary>
    /// <returns>A policy granting unrestricted read-write access with default limits.</returns>
    private static PathPolicy Policy()
    {
        return new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
    }
}
