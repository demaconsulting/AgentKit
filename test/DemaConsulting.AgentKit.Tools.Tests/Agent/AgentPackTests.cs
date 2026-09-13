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
    ///     Builds a permissive policy for scenarios where containment is not the subject.
    /// </summary>
    /// <returns>A policy granting unrestricted read-write access with default limits.</returns>
    private static PathPolicy Policy()
    {
        return new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
    }
}
