using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Unit tests for the <see cref="AgentRunTool"/> class and the <see cref="ChildAgentRequest"/>
///     it hands the host's runner.
/// </summary>
/// <remarks>
///     Most scenarios build the tool directly through its internal factory so that the depth, the
///     profiles and the child-composition seam can each be varied on their own. The seam is the
///     family's isolation boundary; here it is a stub that records what it was asked for. The
///     request scenarios compose the pack instead, because the request's constructor is internal
///     and a hand-built one would not exercise the path the family actually takes.
/// </remarks>
public class AgentRunToolTests
{
    /// <summary>
    ///     A composer that publishes no tools, for scenarios where the child's tool set is not the
    ///     subject.
    /// </summary>
    private static readonly ChildToolComposer NoTools = _ => [];

    /// <summary>
    ///     A runner that reports a fixed answer, for scenarios where the child's answer is not the
    ///     subject.
    /// </summary>
    private static readonly Func<ChildAgentRequest, CancellationToken, Task<string?>> FixedAnswer = (_, _) => Task.FromResult<string?>("done");

    /// <summary>
    ///     Proves the tool carries the published name, and that its description names the profiles
    ///     the model may choose from so that choosing does not require a wasted turn.
    /// </summary>
    [Fact]
    public void AgentRunTool_Create_DescriptionNamesTheAvailableProfiles()
    {
        // Arrange: two profiles, one of them described
        var profiles = new[]
        {
            new AgentProfile("reviewer", "Review.", [], description: "Reads and reports."),
            new AgentProfile("writer", "Write.", []),
        };

        // Act: build the tool
        var tool = AgentRunTool.Create(Policy(), profiles, FixedAnswer, NoTools, depth: 0);

        // Assert: both names, and the description that was given, are in front of the model
        Assert.Equal("agent_run", tool.Name);
        Assert.Equal(AgentRunTool.ToolName, tool.Name);
        Assert.Contains("'reviewer' (Reads and reports.)", tool.Description, StringComparison.Ordinal);
        Assert.Contains("'writer'", tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a composition registering no profiles still describes itself honestly rather than
    ///     trailing off.
    /// </summary>
    [Fact]
    public void AgentRunTool_Create_NoProfiles_DescriptionStatesThereAreNone()
    {
        // Arrange / Act: build the tool with an empty registration
        var tool = AgentRunTool.Create(Policy(), [], FixedAnswer, NoTools, depth: 0);

        // Assert: the model is told delegation is not possible
        Assert.Contains("none are registered", tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every part the family supplies is mandatory, since a missing one is a defect in
    ///     this library rather than something a composing application did.
    /// </summary>
    [Fact]
    public void AgentRunTool_Create_MissingParts_Throw()
    {
        // Act / Assert: each argument is required, and a negative depth has no meaning
        Assert.Throws<ArgumentNullException>(
            () => AgentRunTool.Create(null!, [], FixedAnswer, NoTools, 0));
        Assert.Throws<ArgumentNullException>(
            () => AgentRunTool.Create(Policy(), null!, FixedAnswer, NoTools, 0));
        Assert.Throws<ArgumentNullException>(
            () => AgentRunTool.Create(Policy(), [], null!, NoTools, 0));
        Assert.Throws<ArgumentNullException>(
            () => AgentRunTool.Create(Policy(), [], FixedAnswer, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AgentRunTool.Create(Policy(), [], FixedAnswer, NoTools, -1));
    }

    /// <summary>
    ///     Proves an unknown profile is refused with a plain statement of fact naming the profiles
    ///     that do exist, and that the refusal prescribes no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_UnknownProfile_IsRefusedAndNamesTheAvailableProfiles()
    {
        // Arrange: two registered profiles
        var profiles = new[]
        {
            new AgentProfile("reviewer", "Review.", []),
            new AgentProfile("writer", "Write.", []),
        };
        var tool = AgentRunTool.Create(Policy(), profiles, FixedAnswer, NoTools, depth: 0);

        // Act: name a profile the application never registered
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "auditor", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: a returned refusal stating the fact and naming what exists
        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Denied (TargetNotFound): No profile is named 'auditor'. The available profiles are "
            + "'reviewer', 'writer'.",
            text);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves profile names are compared ordinally, so a name means exactly itself.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_ProfileNameCase_IsSignificant()
    {
        // Arrange: one registered profile
        var tool = AgentRunTool.Create(
            Policy(),
            [new AgentProfile("reviewer", "Review.", [])],
            FixedAnswer,
            NoTools,
            depth: 0);

        // Act: name it with different casing
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "Reviewer", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: refused rather than silently matched
        Assert.StartsWith("Denied (TargetNotFound)", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a request missing a profile or a task is refused rather than thrown, and that the
    ///     missing-profile refusal still names what is available.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_MissingProfileOrTask_IsRefused()
    {
        // Arrange: one registered profile
        var tool = AgentRunTool.Create(
            Policy(),
            [new AgentProfile("reviewer", "Review.", [])],
            FixedAnswer,
            NoTools,
            depth: 0);

        // Act: omit each mandatory argument in turn
        var noProfile = await tool.InvokeAsync(
            new AIFunctionArguments { ["task"] = "Check it." },
            TestContext.Current.CancellationToken);
        var noTask = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer" },
            TestContext.Current.CancellationToken);

        // Assert: both are returned refusals
        Assert.Contains("'reviewer'", Assert.IsType<string>(noProfile), StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", Assert.IsType<string>(noTask), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves delegation is refused at the depth ceiling, stating the ceiling and the current
    ///     level as facts, and that nothing is composed or started for a run that is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_AtTheDepthCeiling_IsRefusedWithoutComposingOrStarting()
    {
        // Arrange: a host that forbids delegation entirely, and instruments both seams
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxAgentDepth: 0));

        var composed = false;
        var started = false;
        var tool = AgentRunTool.Create(
            policy,
            [new AgentProfile("reviewer", "Review.", [])],
            (_, _) =>
            {
                started = true;
                return Task.FromResult<string?>("done");
            },
            _ =>
            {
                composed = true;
                return [];
            },
            depth: 0);

        // Act: attempt to delegate
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: refused with both numbers stated, and nothing built or run
        Assert.Equal(
            "Denied (InvalidRequest): Delegation is limited to 0 levels and this agent is already "
            + "at level 0.",
            Assert.IsType<string>(result));
        Assert.False(composed);
        Assert.False(started);
    }

    /// <summary>
    ///     Proves an agent one level below the ceiling may still delegate, so the ceiling bounds the
    ///     chain rather than forbidding it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_BelowTheDepthCeiling_IsPermitted()
    {
        // Arrange: a ceiling of two, reached from depth one
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxAgentDepth: 2));

        var tool = AgentRunTool.Create(
            policy,
            [new AgentProfile("reviewer", "Review.", [])],
            FixedAnswer,
            NoTools,
            depth: 1);

        // Act: delegate one level deeper
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: the child ran and its answer came back
        Assert.Equal("done", result);
    }

    /// <summary>
    ///     Proves a child that finished without reporting anything is described as such rather than
    ///     as a failure, so the parent does not retry work that was already done.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_ChildSaidNothing_IsReportedAsSuch()
    {
        // Arrange: a runner whose agent finishes silently
        var tool = AgentRunTool.Create(
            Policy(),
            [new AgentProfile("reviewer", "Review.", [])],
            (_, _) => Task.FromResult<string?>(null),
            NoTools,
            depth: 0);

        // Act: delegate
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: stated as a quiet finish, not a denial
        Assert.Equal("The 'reviewer' agent finished without reporting anything.", result);
    }

    /// <summary>
    ///     Proves a child's answer is bounded by the composition's result ceiling and refused rather
    ///     than truncated, because a parent cannot tell it is reading half an answer.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_OversizedAnswer_IsRefusedRatherThanTruncated()
    {
        // Arrange: a tight result ceiling and a talkative child
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxResultCharacters: 32));

        var tool = AgentRunTool.Create(
            policy,
            [new AgentProfile("reviewer", "Review.", [])],
            (_, _) => Task.FromResult<string?>(new string('x', 100)),
            NoTools,
            depth: 0);

        // Act: delegate
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Check it." },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming both numbers, and no partial answer
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("100 characters", text, StringComparison.Ordinal);
        Assert.Contains("32-character result limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the profile the model named is the profile whose instructions and declared tools
    ///     shape the child, and that the task is the only part the parent supplies.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentRunTool_Run_ComposesTheNamedProfileOnly()
    {
        // Arrange: two profiles, with the composer recording which one it was asked for
        var profiles = new[]
        {
            new AgentProfile("reviewer", "Review.", ["text_file_read"]),
            new AgentProfile("writer", "Write.", ["text_file_create"]),
        };

        AgentProfile? asked = null;
        var tool = AgentRunTool.Create(
            Policy(),
            profiles,
            FixedAnswer,
            profile =>
            {
                asked = profile;
                return [];
            },
            depth: 0);

        // Act: delegate to the second one
        await tool.InvokeAsync(
            new AIFunctionArguments { ["profile"] = "writer", ["task"] = "Write it." },
            TestContext.Current.CancellationToken);

        // Assert: the composition was asked for the named profile and no other
        Assert.NotNull(asked);
        Assert.Equal("writer", asked.Name);
    }

    /// <summary>
    ///     Proves the request a host receives carries the profile's name and instructions, the task
    ///     the parent stated, and the child's depth — everything a host needs to build an agent and
    ///     nothing the parent agent authored beyond the task.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChildAgentRequest_Properties_DescribeTheChildTheHostMustBuild()
    {
        // Arrange: one profile, and a runner that keeps the request it was handed
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var profile = new AgentProfile("worker", "You do one job.", [TodoListTool.ToolName]);

        ChildAgentRequest? seen = null;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
        {
            seen = request;
            return Task.FromResult<string?>("done");
        };

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, [new TodoPack()]))
            .Build();

        // Act: delegate
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Count the files." },
            TestContext.Current.CancellationToken);

        // Assert: the host was told who, what, and how deep — and given the child's own tools
        Assert.NotNull(seen);
        Assert.Equal("worker", seen.ProfileName);
        Assert.Equal("You do one job.", seen.Instructions);
        Assert.Equal("Count the files.", seen.Task);
        Assert.Equal(1, seen.Depth);
        Assert.Equal([TodoListTool.ToolName], seen.Tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a runner that observes cancellation is given the parent tool call's token, so a
    ///     cancelled parent turn does not leave a child running.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChildAgentRequest_Runner_ObservesTheCallersCancellationToken()
    {
        // Arrange: a runner that reports whether it was given a live token
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var profile = new AgentProfile("worker", "You do one job.", []);

        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (_, token) =>
        {
            cancelled = token.IsCancellationRequested;
            return Task.FromResult<string?>("done");
        };

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, []))
            .Build();

        await cancellation.CancelAsync();

        // Act: delegate with an already-cancelled token
        await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            cancellation.Token);

        // Assert: the runner saw the caller's cancellation rather than a token of its own
        Assert.True(cancelled);
    }

    /// <summary>
    ///     Proves a failure inside the host's runner propagates rather than being converted into a
    ///     refusal, because this library cannot tell a transient outage from a misconfiguration and
    ///     must not tell the model something it does not know.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChildAgentRequest_Runner_HostFailure_Propagates()
    {
        // Arrange: a runner that cannot reach its provider
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var profile = new AgentProfile("worker", "You do one job.", []);
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (_, _) => throw new InvalidOperationException("no credentials");

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, []))
            .Build();

        // Act / Assert: the host's condition reaches the host, not the model
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await tools[0].InvokeAsync(
                new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
                TestContext.Current.CancellationToken));
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
