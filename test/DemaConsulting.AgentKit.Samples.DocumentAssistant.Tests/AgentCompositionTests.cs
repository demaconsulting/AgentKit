namespace DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests;

/// <summary>
///     Unit tests for the system instructions the document-assistant sample builds for a run.
/// </summary>
/// <remarks>
///     The instructions are built per run rather than held as a constant precisely so they can name
///     the two locations the run granted. These scenarios pin that: both absolute paths appear, the
///     workspace access level follows the grant, and the statements the sample relies on for its
///     demonstrations — no shell or web tool, discovery by a no-argument listing, the
///     relative-versus-absolute dialect, the figure-preparation loop, and reading a refusal instead
///     of retrying it — survive the
///     change from a constant to a builder.
/// </remarks>
public class AgentCompositionTests
{
    /// <summary>
    ///     A workspace path unlike any real one, so a match in the instructions cannot be accidental.
    /// </summary>
    private const string WorkspacePath = "/fixture-roots/documents-workspace";

    /// <summary>
    ///     A session path outside the workspace, as the sample's real session folder always is.
    /// </summary>
    private const string SessionPath = "/fixture-roots/assistant-session";

    /// <summary>
    ///     Proves a read-write run states both locations, with the workspace as read-write.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_WritableWorkspace_NamesBothLocationsWithAccess()
    {
        // Arrange / Act: build the instructions for a run granting the workspace read-write
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);

        // Assert: an application that knows its locations says so, rather than leaving the agent
        // to discover a session folder that may be empty and therefore unlisted
        Assert.Multiple(
            () => Assert.Contains(WorkspacePath, instructions, StringComparison.Ordinal),
            () => Assert.Contains(SessionPath, instructions, StringComparison.Ordinal),
            () => Assert.Contains("read-write", instructions, StringComparison.Ordinal),
            () => Assert.DoesNotContain("read-only", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the read-only workspace flag is reflected in the stated access level.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_ReadOnlyWorkspace_StatesTheWorkspaceIsReadOnly()
    {
        // Arrange / Act: build the instructions for a run granting the workspace read-only
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, true);

        // Assert: the workspace is described as read-only while the session stays writable
        Assert.Multiple(
            () => Assert.Contains($"'{WorkspacePath}' (read-only)", instructions, StringComparison.Ordinal),
            () => Assert.Contains($"'{SessionPath}' (read-write)", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the instructions are a function of the run rather than a fixed constant.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_DifferentLocations_ProduceDifferentInstructions()
    {
        // Arrange / Act: build for two different pairs of locations
        var first = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);
        var second = AgentComposition.BuildInstructions("/other/workspace", "/other/session", false);

        // Assert: naming the locations is only honest if the text follows them
        Assert.Multiple(
            () => Assert.NotEqual(first, second),
            () => Assert.Contains("/other/session", second, StringComparison.Ordinal),
            () => Assert.DoesNotContain(SessionPath, second, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the truthful capability statement the tool-inventory demonstration depends on is
    ///     still present.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_DeniesShellAndWebCapabilities()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);

        // Assert: the sample's "do you have a shell?" demonstration rests on this sentence
        Assert.Contains(
            "no shell, terminal, code-execution, or web/fetch tool",
            instructions,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves discovery guidance and the refusal-handling guidance survived the rewrite.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_KeepsDiscoveryAndRefusalGuidance()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);

        // Assert: discovery is still requested (it demonstrates the dialect) and a refusal is still
        // something to read rather than something to retry
        Assert.Multiple(
            () => Assert.Contains(
                "file_list with no directory argument",
                instructions,
                StringComparison.Ordinal),
            () => Assert.Contains("Do not retry the identical call", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the relative-versus-absolute dialect guidance survived the rewrite.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_KeepsTheRelativeAndAbsoluteDialectGuidance()
    {
        // Arrange / Act: build the instructions
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);

        // Assert: relative names for the workspace, the absolute path for the session folder, and
        // the reason a relative name cannot reach the session folder
        Assert.Multiple(
            () => Assert.Contains("plain relative names", instructions, StringComparison.Ordinal),
            () => Assert.Contains("full absolute path", instructions, StringComparison.Ordinal),
            () => Assert.Contains(
                "always interpreted against the workspace",
                instructions,
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the figure-preparation workflow is discoverable from the instructions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The guidance is judged as one span of prose rather than as three substrings that could
    ///     each have come from somewhere else in the instructions — <c>.png</c> in particular
    ///     appears in unrelated examples elsewhere in the sample's documentation, and a test that
    ///     found it anywhere would pass on prose that said nothing about cropping.
    ///     </para>
    ///     <para>
    ///     Each of the three assertions is a separate way the guidance could be wrong. Without the
    ///     tool named, the workflow is not discoverable at all. Without the session folder named,
    ///     the destination the agent is pointed at would be the workspace, which is refused under
    ///     <c>--read-only-workspace</c> — the session folder is the only location writable in both
    ///     modes. Without the extension stated, the agent learns the rule only by being refused.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AgentComposition_BuildInstructions_AnyRun_DescribesTheCropToFileWorkflow()
    {
        // Arrange: the instructions for an ordinary run
        var instructions = AgentComposition.BuildInstructions(WorkspacePath, SessionPath, false);

        // Act: isolate the figure-preparation guidance, which sits between the create-in-the-
        // session-folder thread it extends and the refusal-handling guidance that follows it
        var start = instructions.IndexOf("When an image holds", StringComparison.Ordinal);
        Assert.True(start >= 0, "The instructions describe no figure-preparation workflow.");

        var remainder = instructions[start..];
        var end = remainder.IndexOf("When a tool refuses", StringComparison.Ordinal);
        Assert.True(end > 0, "The figure-preparation guidance is not followed by the refusal guidance.");

        var guidance = remainder[..end];

        // Assert: the tool is named, the destination is the location writable in both modes, and
        // the extension rule is stated up front rather than learned from a refusal
        Assert.Multiple(
            () => Assert.Contains("image_crop", guidance, StringComparison.Ordinal),
            () => Assert.Contains("session folder", guidance, StringComparison.Ordinal),
            () => Assert.Contains(".png", guidance, StringComparison.Ordinal));
    }
}
