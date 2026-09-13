using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Subsystem-level integration tests for the agent tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy and one host capability
///     declaration, then invoked through the published tool list. They assert the family-wide
///     properties — the capability gate, the depth ceiling across a real chain of agents, and the
///     fact that a child's tools are composed for the child rather than handed down.
/// </remarks>
public class AgentTests
{
    /// <summary>
    ///     A runner that reports a fixed answer, for scenarios where the child's answer is not the
    ///     subject.
    /// </summary>
    private static readonly Func<ChildAgentRequest, CancellationToken, Task<string?>> FixedAnswer = (_, _) => Task.FromResult<string?>("done");

    /// <summary>
    ///     Proves a host that declares delegation receives the family's single tool.
    /// </summary>
    [Fact]
    public void Agent_Family_ComposedThroughBuilder_PublishesTheRunTool()
    {
        // Arrange / Act: attach the family to a delegating host
        var tools = new ToolPackBuilder(Policy())
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([], FixedAnswer, []))
            .Build();

        // Assert: one tool, under the family prefix
        Assert.Equal([AgentRunTool.ToolName], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a host that has not declared delegation receives none of the family's tools, so
    ///     an application that cannot start a second agent never offers one.
    /// </summary>
    [Fact]
    public void Agent_Family_HostWithoutDelegation_ContributesNoTools()
    {
        // Arrange / Act: attach the family to a host that declares nothing
        var tools = new ToolPackBuilder(Policy())
            .Add(new AgentPack([], FixedAnswer, []))
            .Build();

        // Assert: the capability requirement is unmet, so the family contributes nothing
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves the family's tool carries a valid name and a description.
    /// </summary>
    [Fact]
    public void Agent_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        // Arrange / Act: attach the family
        var tools = new ToolPackBuilder(Policy())
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([], FixedAnswer, []))
            .Build();

        // Assert: the name is well formed, carries the family prefix, and is described
        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(AgentPack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves a delegated run reaches the host with the profile's instructions and the parent's
    ///     task, and returns the child's own words to the parent.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Agent_Family_Delegation_ReturnsTheFinalTextOfTheChild()
    {
        // Arrange: one registered profile and a host that answers by quoting its instructions
        var profile = new AgentProfile("reviewer", "You review files.", ["notes_read"]);
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = (request, _) =>
            Task.FromResult<string?>(request.Instructions + " | " + request.Task);

        var tools = new ToolPackBuilder(Policy())
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, [new StubToolPack("notes", "read")]))
            .Build();

        // Act: delegate
        var result = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "reviewer", ["task"] = "Check the notes." },
            TestContext.Current.CancellationToken);

        // Assert: the application authored the instructions and the parent supplied only the task
        Assert.Equal("You review files. | Check the notes.", result);
    }

    /// <summary>
    ///     Proves a child whose profile admits the delegation tool receives it one level deeper, and
    ///     that the chain is refused at the ceiling rather than running away.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Agent_Family_DelegationChain_IsRefusedAtTheDepthCeiling()
    {
        // Arrange: a ceiling of two, and a profile that may itself delegate. The host runs each
        // child by calling that child's own agent_run tool, so the chain is a real one.
        var policy = new PathPolicy(
            Path.GetTempPath(),
            [PathRule.Unrestricted(AccessLevel.ReadWrite)],
            new ToolLimits(maxAgentDepth: 2));

        var profile = new AgentProfile("worker", "You work, or delegate.", [AgentRunTool.ToolName]);

        var answers = new List<string>();
        Func<ChildAgentRequest, CancellationToken, Task<string?>> runner = async (request, token) =>
        {
            // Each child immediately delegates again, until one of them is refused.
            var child = request.Tools.Single(tool => tool.Name == AgentRunTool.ToolName);
            var result = await child.InvokeAsync(
                new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Go deeper." },
                token);

            var text = Assert.IsType<string>(result);
            answers.Add(text);
            return text;
        };

        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(new AgentPack([profile], runner, []))
            .Build();

        // Act: start the chain from the root
        var final = await tools[0].InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Go deeper." },
            TestContext.Current.CancellationToken);

        // Assert: the agent at the ceiling refused, stating both numbers, and the refusal came back
        // up the chain rather than an unbounded run continuing
        Assert.Equal(2, answers.Count);
        Assert.Equal(
            "Denied (InvalidRequest): Delegation is limited to 2 levels and this agent is already "
            + "at level 2.",
            answers[0]);
        Assert.Equal(answers[0], final);
    }

    /// <summary>
    ///     Proves a child's tools are composed for that child rather than being the parent's own
    ///     tools filtered, by showing the registered pack is asked for tools once per delegated run
    ///     and never handed the parent's instances.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Agent_Family_ChildTools_AreComposedForTheChild()
    {
        // Arrange: a pack that records every composition it is asked for
        var pack = new StubToolPack("notes", "read");
        var profile = new AgentProfile("worker", "You work.", ["notes_read"]);

        var parentTools = new ToolPackBuilder(Policy())
            .WithHostCapabilities(HostCapabilities.Delegation)
            .Add(pack)
            .Add(new AgentPack([profile], CaptureRunner(out var captured), [pack]))
            .Build();

        var parentNotesRead = parentTools.Single(tool => tool.Name == "notes_read");

        // Act: delegate once
        await parentTools.Single(tool => tool.Name == AgentRunTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["profile"] = "worker", ["task"] = "Do it." },
            TestContext.Current.CancellationToken);

        // Assert: the pack was composed twice — once for the parent, once for the child — and the
        // child's tool is not the parent's instance
        Assert.Equal(2, pack.Policies.Count);
        Assert.NotSame(parentNotesRead, Assert.Single(captured).Tools[0]);
    }

    /// <summary>
    ///     Creates a runner that records every request it is handed.
    /// </summary>
    /// <param name="captured">On return, the list the runner appends each request to.</param>
    /// <returns>A runner that records and then answers.</returns>
    private static Func<ChildAgentRequest, CancellationToken, Task<string?>> CaptureRunner(out List<ChildAgentRequest> captured)
    {
        var requests = new List<ChildAgentRequest>();
        captured = requests;

        return (request, _) =>
        {
            requests.Add(request);
            return Task.FromResult<string?>("done");
        };
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
