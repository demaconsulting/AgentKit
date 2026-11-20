namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for the release policy of <see cref="CopilotSessionChannel"/>: a retained channel
///     detaches without deleting, a deleting channel detaches and then deletes, and neither step's
///     failure escapes.
/// </summary>
/// <remarks>
///     The production channel wraps the SDK's session type, which cannot be constructed in a test,
///     so the policy is exercised through <see cref="CopilotSessionChannel.ReleaseAsync"/> with the
///     two steps supplied as delegates.
/// </remarks>
public class CopilotTurnChannelTests
{
    /// <summary>
    ///     Proves a channel opened for an agent releases its session handle and asks for no deletion,
    ///     so the conversation stays resumable.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_RetainedChannel_DetachesWithoutDeleting()
    {
        // Arrange
        var steps = new List<string>();

        // Act: a retained channel has no deletion step
        await CopilotSessionChannel.ReleaseAsync(
            () =>
            {
                steps.Add("detach");
                return ValueTask.CompletedTask;
            },
            delete: null);

        // Assert
        Assert.Equal(["detach"], steps);
    }

    /// <summary>
    ///     Proves a channel opened for the engine or the summarizer releases its session handle and
    ///     then deletes the session, in that order.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_DeletingChannel_DetachesThenDeletes()
    {
        // Arrange
        var steps = new List<string>();

        // Act
        await CopilotSessionChannel.ReleaseAsync(
            () =>
            {
                steps.Add("detach");
                return ValueTask.CompletedTask;
            },
            () =>
            {
                steps.Add("delete");
                return Task.CompletedTask;
            });

        // Assert
        Assert.Equal(["detach", "delete"], steps);
    }

    /// <summary>
    ///     Proves release never throws: a detach that fails does not stop the deletion, and a deletion
    ///     that fails is swallowed, because release runs where no failure can be acted on.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_FailingSteps_DoNotThrow()
    {
        // Arrange
        var deleted = false;

        // Act: both steps fail, the second only after recording that it ran
        await CopilotSessionChannel.ReleaseAsync(
            () => ValueTask.FromException(new IOException("transport gone")),
            () =>
            {
                deleted = true;
                return Task.FromException(new IOException("runtime gone"));
            });

        // Assert: reaching here is the assertion that nothing escaped; the deletion was attempted
        Assert.True(deleted);
    }
}
