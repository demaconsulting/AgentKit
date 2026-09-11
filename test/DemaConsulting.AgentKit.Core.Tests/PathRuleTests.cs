namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="PathRule"/> class.
/// </summary>
/// <remarks>
///     <see cref="PathRule.Allows"/> is documented to take a location that has already been
///     resolved, so these tests build candidate locations from the rule's own
///     <see cref="PathRule.Root"/>. That keeps the tests independent of whether the temporary
///     directory itself happens to sit beneath a link on the host platform.
/// </remarks>
public class PathRuleTests
{
    /// <summary>
    ///     Proves that an unrestricted rule permits an arbitrary location.
    /// </summary>
    [Fact]
    public void PathRule_Allows_UnrestrictedRule_AnyPath_ReturnsTrue()
    {
        // Arrange: a rule with no location constraint and no denied patterns
        var rule = PathRule.Unrestricted();
        var candidate = RealPathResolver.Resolve(Path.Combine(Path.GetTempPath(), "anywhere.txt"));

        // Act: test an arbitrary location
        var allowed = rule.Allows(candidate);

        // Assert: no location constraint means the location is permitted
        Assert.Null(rule.Root);
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that an unrestricted rule still refuses a location matching a denied pattern.
    /// </summary>
    /// <remarks>
    ///     Wide read access is only safe if credential material can still be excluded, so an
    ///     unrestricted rule must remain able to carry denied patterns.
    /// </remarks>
    [Fact]
    public void PathRule_Allows_UnrestrictedRuleWithDenyPattern_MatchingPath_ReturnsFalse()
    {
        // Arrange: an unrestricted rule that nonetheless excludes key material
        var rule = PathRule.Unrestricted(["*.key"]);
        var candidate = RealPathResolver.Resolve(Path.Combine(Path.GetTempPath(), "server.key"));

        // Act: test a location matching the denied pattern
        var allowed = rule.Allows(candidate);

        // Assert: the pattern overrides the absence of a location constraint
        Assert.False(allowed);
        Assert.Equal("*.key", Assert.Single(rule.DenyPatterns));
    }

    /// <summary>
    ///     Proves that a rooted rule permits a location inside its root.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedRule_PathInsideRoot_ReturnsTrue()
    {
        // Arrange: a rule confined to a temporary root
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root);
        var candidate = Path.Combine(rule.Root!, "nested", "file.txt");

        // Act: test a location beneath the root
        var allowed = rule.Allows(candidate);

        // Assert: contained locations are permitted
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a rooted rule permits the root location itself.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedRule_RootItself_ReturnsTrue()
    {
        // Arrange: a rule confined to a temporary root
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root);

        // Act: test the root itself, which a listing operation needs to reach
        var allowed = rule.Allows(rule.Root!);

        // Assert: the root is part of the permitted location, not merely its boundary
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a rooted rule refuses a location outside its root.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedRule_PathOutsideRoot_ReturnsFalse()
    {
        // Arrange: a rule confined to the root, and a location in the sibling directory
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root);
        var candidate = RealPathResolver.Resolve(Path.Combine(fixture.Outside, "file.txt"));

        // Act: test a location outside the root
        var allowed = rule.Allows(candidate);

        // Assert: locations outside the root are refused
        Assert.False(allowed);
    }

    /// <summary>
    ///     Proves that a sibling location whose name merely starts with the root's name is not
    ///     treated as contained.
    /// </summary>
    /// <remarks>
    ///     Without the trailing separator in the containment test, a root of
    ///     <c>allowed-root</c> would wrongly contain <c>allowed-root-evil</c>.
    /// </remarks>
    [Fact]
    public void PathRule_Allows_RootedRule_SiblingWithSharedPrefix_ReturnsFalse()
    {
        // Arrange: a rule confined to the root, and a sibling sharing the root's name prefix
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root);
        var candidate = rule.Root + "-evil" + Path.DirectorySeparatorChar + "file.txt";

        // Act: test the sibling location
        var allowed = rule.Allows(candidate);

        // Assert: a shared name prefix is not containment
        Assert.False(allowed);
    }

    /// <summary>
    ///     Proves that a denied pattern matching an enclosing directory name refuses the whole
    ///     subtree beneath it.
    /// </summary>
    [Fact]
    public void PathRule_Allows_DenyPatternMatchingDirectorySegment_ReturnsFalse()
    {
        // Arrange: a rooted rule that excludes a repository metadata directory
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root, [".git"]);
        var candidate = Path.Combine(rule.Root!, ".git", "config");

        // Act: test a location inside the excluded directory
        var allowed = rule.Allows(candidate);

        // Assert: excluding a directory excludes everything within it
        Assert.False(allowed);
    }

    /// <summary>
    ///     Proves that a denied pattern matching a file name refuses that file even inside the
    ///     permitted location.
    /// </summary>
    [Fact]
    public void PathRule_Allows_DenyPatternMatchingFileName_ReturnsFalse()
    {
        // Arrange: a rooted rule that excludes key material by name
        using var fixture = new ReparsePointFixture();
        var rule = PathRule.Rooted(fixture.Root, ["*.key"]);
        var candidate = Path.Combine(rule.Root!, "nested", "server.key");

        // Act: test a contained location that matches the denied pattern
        var allowed = rule.Allows(candidate);

        // Assert: the pattern overrides containment
        Assert.False(allowed);
    }

    /// <summary>
    ///     Proves that a root that is itself reached through a link still permits its own
    ///     contents.
    /// </summary>
    /// <remarks>
    ///     Containment is judged on real locations, so the root must be resolved when the rule
    ///     is created; otherwise a legitimately linked working directory would permit nothing.
    /// </remarks>
    [Fact]
    public void PathRule_Rooted_RootReachedThroughLink_AllowsContainedPath()
    {
        // Arrange: a rule whose configured root is a link pointing at the outside directory
        using var fixture = new ReparsePointFixture();
        var link = fixture.CreateDirectoryLink("linked-root", fixture.Outside);
        var rule = PathRule.Rooted(link);
        var candidate = RealPathResolver.Resolve(Path.Combine(fixture.Outside, "file.txt"));

        // Act: test a location inside the link's real target
        var allowed = rule.Allows(candidate);

        // Assert: the rule resolved its root and permits the target's real contents
        Assert.NotEqual(Path.GetFullPath(link), rule.Root);
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a null root is rejected as a programming error.
    /// </summary>
    [Fact]
    public void PathRule_Rooted_NullRoot_ThrowsArgumentNullException()
    {
        // Act & Assert: a rule with no location cannot be silently treated as unrestricted
        Assert.Throws<ArgumentNullException>(() => PathRule.Rooted(null!));
    }

    /// <summary>
    ///     Proves that an empty root is rejected as a programming error.
    /// </summary>
    [Fact]
    public void PathRule_Rooted_EmptyRoot_ThrowsArgumentException()
    {
        // Act & Assert: the empty-string boundary is distinct from the null case
        Assert.Throws<ArgumentException>(() => PathRule.Rooted(string.Empty));
    }

    /// <summary>
    ///     Proves that a null denied pattern is rejected as a programming error.
    /// </summary>
    /// <remarks>
    ///     An empty or missing pattern cannot express a meaningful exclusion, so accepting one
    ///     would quietly weaken a rule the caller believed was tightened.
    /// </remarks>
    [Fact]
    public void PathRule_Rooted_NullDenyPattern_ThrowsArgumentException()
    {
        // Arrange: a valid root with an invalid pattern list
        using var fixture = new ReparsePointFixture();

        // Act & Assert: the malformed rule is refused at construction
        Assert.Throws<ArgumentException>(() => PathRule.Rooted(fixture.Root, [null!]));
    }
}
