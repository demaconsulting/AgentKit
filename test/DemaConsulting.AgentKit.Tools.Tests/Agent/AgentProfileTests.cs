using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Agent;

namespace DemaConsulting.AgentKit.Tools.Tests.Agent;

/// <summary>
///     Unit tests for the <see cref="AgentProfile"/> class.
/// </summary>
public class AgentProfileTests
{
    /// <summary>
    ///     Proves a profile keeps exactly what the application registered, and that omitting the
    ///     optional parts yields a child that inherits its parent's policy.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_StatedParts_AreKept()
    {
        // Arrange / Act: register the profile from the brief's own example
        var profile = new AgentProfile(
            name: "reviewer",
            instructions: "You review source files and report findings.",
            tools: ["text_file_read", "text_file_search"]);

        // Assert: everything stated is kept, and nothing is invented
        Assert.Equal("reviewer", profile.Name);
        Assert.Equal("You review source files and report findings.", profile.Instructions);
        Assert.Equal(["text_file_read", "text_file_search"], profile.Tools);
        Assert.Empty(profile.Grants);
        Assert.Null(profile.Description);
    }

    /// <summary>
    ///     Proves a profile that states grants and a description keeps both.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_GrantsAndDescription_AreKept()
    {
        // Arrange: a directory the child is to be narrowed to
        var root = Path.GetTempPath();

        // Act: register a narrowed profile
        var profile = new AgentProfile(
            name: "note-taker",
            instructions: "You write short notes.",
            tools: ["text_file_create"],
            grants: [PathRule.ReadWrite(root)],
            description: "Writes notes into the session directory.");

        // Assert: the narrowing and the description survive
        Assert.Single(profile.Grants);
        Assert.Equal(AccessLevel.ReadWrite, profile.Grants[0].Access);
        Assert.Equal("Writes notes into the session directory.", profile.Description);
    }

    /// <summary>
    ///     Proves a profile with no tools is a legitimate registration rather than a mistake, since
    ///     judging or summarizing text passed in a task needs no tools at all.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_NoTools_IsPermitted()
    {
        // Arrange / Act: register an agent that can only think and answer
        var profile = new AgentProfile("judge", "You answer yes or no.", []);

        // Assert: accepted, with an empty tool set
        Assert.Empty(profile.Tools);
    }

    /// <summary>
    ///     Proves a nameless or silent profile is refused, since neither could be selected or given
    ///     a job.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_MissingNameOrInstructions_Throws()
    {
        // Act / Assert: both halves of a profile's identity are mandatory
        Assert.Throws<ArgumentNullException>(() => new AgentProfile(null!, "Do the thing.", []));
        Assert.Throws<ArgumentException>(() => new AgentProfile(string.Empty, "Do the thing.", []));
        Assert.Throws<ArgumentNullException>(() => new AgentProfile("worker", null!, []));
        Assert.Throws<ArgumentException>(() => new AgentProfile("worker", string.Empty, []));
    }

    /// <summary>
    ///     Proves a tool name that could never match an attached tool is refused at registration
    ///     rather than silently narrowing a child to nothing.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_EmptyToolName_ThrowsArgumentException()
    {
        // Act / Assert: an empty name is a mistake in the application's configuration
        Assert.Throws<ArgumentNullException>(() => new AgentProfile("worker", "Work.", null!));
        Assert.Throws<ArgumentException>(() => new AgentProfile("worker", "Work.", ["text_file_read", ""]));
    }

    /// <summary>
    ///     Proves a null grant is refused, since a child's reach could not then be determined.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_NullGrant_ThrowsArgumentException()
    {
        // Act / Assert: a hole in the grant list is a configuration error
        Assert.Throws<ArgumentException>(
            () => new AgentProfile("worker", "Work.", [], [null!]));
    }

    /// <summary>
    ///     Proves the collections a profile was built from cannot be altered afterwards, so a
    ///     registration cannot change under the family that holds it.
    /// </summary>
    [Fact]
    public void AgentProfile_Constructor_SourceCollections_AreCopied()
    {
        // Arrange: mutable sources
        var tools = new List<string> { "text_file_read" };
        var grants = new List<PathRule> { PathRule.ReadOnly(Path.GetTempPath()) };
        var profile = new AgentProfile("reviewer", "Review.", tools, grants);

        // Act: mutate the sources after registration
        tools.Add("text_file_create");
        grants.Add(PathRule.Unrestricted(AccessLevel.ReadWrite));

        // Assert: the profile is unchanged
        Assert.Single(profile.Tools);
        Assert.Single(profile.Grants);
    }
}
