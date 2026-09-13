using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Unit tests for the <see cref="ChildAgentRequest"/> class and the
///     runner it is handed to.
/// </summary>
public class ChildAgentRequestTests
{
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
}
