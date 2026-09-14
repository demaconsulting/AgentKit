namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="PathRule"/> class.
/// </summary>
/// <remarks>
///     <see cref="PathRule.Allows"/> is documented to take a location that has already been
///     resolved, so these tests build candidate locations from the grant's own
///     <see cref="PathRule.Root"/>. That keeps the tests independent of whether the temporary
///     directory itself happens to sit beneath a link on the host platform.
///     <para>
///     A grant carries an <see cref="AccessLevel"/> that is permission only: it decides whether a
///     location may be written as well as read, and never affects containment, which
///     <see cref="PathRule.Allows"/> judges identically at either level.
///     </para>
/// </remarks>
public class PathRuleTests
{
    /// <summary>
    ///     Proves that an unrestricted grant permits an arbitrary location.
    /// </summary>
    [Fact]
    public void PathRule_Allows_UnrestrictedGrant_AnyPath_ReturnsTrue()
    {
        // Arrange: a grant with no location constraint and no denied patterns
        var rule = PathRule.Unrestricted(AccessLevel.ReadOnly);
        var candidate = RealPathResolver.Resolve(Path.Combine(Path.GetTempPath(), "anywhere.txt"));

        // Act: test an arbitrary location
        var allowed = rule.Allows(candidate);

        // Assert: no location constraint means the location is permitted
        Assert.Null(rule.Root);
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that an unrestricted grant carries the access level it was created with.
    /// </summary>
    [Theory]
    [InlineData(AccessLevel.ReadOnly)]
    [InlineData(AccessLevel.ReadWrite)]
    public void PathRule_Unrestricted_CarriesRequestedAccessLevel(AccessLevel access)
    {
        // Arrange & Act: an unrestricted grant of the requested level
        var rule = PathRule.Unrestricted(access);

        // Assert: the level is carried, and permission never implies a location
        Assert.Equal(access, rule.Access);
        Assert.Null(rule.Root);
    }

    /// <summary>
    ///     Proves that an unrestricted grant still refuses a location matching a denied pattern.
    /// </summary>
    /// <remarks>
    ///     Wide read access is only safe if credential material can still be excluded, so an
    ///     unrestricted grant must remain able to carry denied patterns.
    /// </remarks>
    [Fact]
    public void PathRule_Allows_UnrestrictedGrantWithDenyPattern_MatchingPath_ReturnsFalse()
    {
        // Arrange: an unrestricted grant that nonetheless excludes key material
        var rule = PathRule.Unrestricted(AccessLevel.ReadOnly, ["*.key"]);
        var candidate = RealPathResolver.Resolve(Path.Combine(Path.GetTempPath(), "server.key"));

        // Act: test a location matching the denied pattern
        var allowed = rule.Allows(candidate);

        // Assert: the pattern overrides the absence of a location constraint
        Assert.False(allowed);
        Assert.Equal("*.key", Assert.Single(rule.DenyPatterns));
    }

    /// <summary>
    ///     Proves that <see cref="PathRule.ReadOnly"/> grants read-only access and
    ///     <see cref="PathRule.ReadWrite"/> grants read-write access.
    /// </summary>
    [Fact]
    public void PathRule_RootedFactories_CarryTheirAccessLevel()
    {
        // Arrange: one grant from each rooted factory over the same location
        using var fixture = new TempDirectoryFixture();

        // Act: create a read-only and a read-write grant
        var readOnly = PathRule.ReadOnly(fixture.Root);
        var readWrite = PathRule.ReadWrite(fixture.Root);

        // Assert: each carries the level named by the factory that made it
        Assert.Equal(AccessLevel.ReadOnly, readOnly.Access);
        Assert.Equal(AccessLevel.ReadWrite, readWrite.Access);
    }

    /// <summary>
    ///     Proves that a rooted grant permits a location inside its root, at either access level.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedGrant_PathInsideRoot_ReturnsTrue()
    {
        // Arrange: a grant confined to a temporary root
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadOnly(fixture.Root);
        var candidate = Path.Combine(rule.Root!, "nested", "file.txt");

        // Act: test a location beneath the root
        var allowed = rule.Allows(candidate);

        // Assert: contained locations are permitted
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a rooted grant permits the root location itself.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedGrant_RootItself_ReturnsTrue()
    {
        // Arrange: a grant confined to a temporary root
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadWrite(fixture.Root);

        // Act: test the root itself, which a listing operation needs to reach
        var allowed = rule.Allows(rule.Root!);

        // Assert: the root is part of the permitted location, not merely its boundary
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a rooted grant refuses a location outside its root.
    /// </summary>
    [Fact]
    public void PathRule_Allows_RootedGrant_PathOutsideRoot_ReturnsFalse()
    {
        // Arrange: a grant confined to the root, and a location in the sibling directory
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadOnly(fixture.Root);
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
    public void PathRule_Allows_RootedGrant_SiblingWithSharedPrefix_ReturnsFalse()
    {
        // Arrange: a grant confined to the root, and a sibling sharing the root's name prefix
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadOnly(fixture.Root);
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
        // Arrange: a rooted grant that excludes a repository metadata directory
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadOnly(fixture.Root, [".git"]);
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
        // Arrange: a rooted grant that excludes key material by name
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadWrite(fixture.Root, ["*.key"]);
        var candidate = Path.Combine(rule.Root!, "nested", "server.key");

        // Act: test a contained location that matches the denied pattern
        var allowed = rule.Allows(candidate);

        // Assert: the pattern overrides containment
        Assert.False(allowed);
    }

    /// <summary>
    ///     Proves that a root spelled with relative segments still permits its own contents.
    /// </summary>
    /// <remarks>
    ///     Containment is judged between normalized locations, so the root must be normalized
    ///     when the grant is created; otherwise a grant spelled relatively would permit nothing.
    /// </remarks>
    [Fact]
    public void PathRule_Rooted_RootWithRelativeSegments_AllowsContainedPath()
    {
        // Arrange: a grant whose configured root detours through a parent segment
        using var fixture = new TempDirectoryFixture();
        var detour = Path.Combine(fixture.Root, "..", "outside");
        var rule = PathRule.ReadOnly(detour);
        var candidate = RealPathResolver.Resolve(Path.Combine(fixture.Outside, "file.txt"));

        // Act: test a location inside the grant's normalized root
        var allowed = rule.Allows(candidate);

        // Assert: the grant normalized its root and permits that location's contents
        Assert.Equal(RealPathResolver.Resolve(fixture.Outside), rule.Root);
        Assert.True(allowed);
    }

    /// <summary>
    ///     Proves that a null root is rejected as a programming error.
    /// </summary>
    [Fact]
    public void PathRule_ReadOnly_NullRoot_ThrowsArgumentNullException()
    {
        // Act & Assert: a grant with no location cannot be silently treated as unrestricted
        Assert.Throws<ArgumentNullException>(() => PathRule.ReadOnly(null!));
    }

    /// <summary>
    ///     Proves that an empty root is rejected as a programming error.
    /// </summary>
    [Fact]
    public void PathRule_ReadWrite_EmptyRoot_ThrowsArgumentException()
    {
        // Act & Assert: the empty-string boundary is distinct from the null case
        Assert.Throws<ArgumentException>(() => PathRule.ReadWrite(string.Empty));
    }

    /// <summary>
    ///     Proves that a null denied pattern is rejected as a programming error.
    /// </summary>
    /// <remarks>
    ///     An empty or missing pattern cannot express a meaningful exclusion, so accepting one
    ///     would quietly weaken a grant the caller believed was tightened.
    /// </remarks>
    [Fact]
    public void PathRule_ReadOnly_NullDenyPattern_ThrowsArgumentException()
    {
        // Arrange: a valid root with an invalid pattern list
        using var fixture = new TempDirectoryFixture();

        // Act & Assert: the malformed grant is refused at construction
        Assert.Throws<ArgumentException>(() => PathRule.ReadOnly(fixture.Root, [null!]));
    }

    /// <summary>
    ///     Proves that a rooted grant describes its location and access level for a denial.
    /// </summary>
    [Fact]
    public void PathRule_Describe_RootedGrant_NamesLocationAndLevel()
    {
        // Arrange: a read-only grant over a temporary root
        using var fixture = new TempDirectoryFixture();
        var rule = PathRule.ReadOnly(fixture.Root);

        // Act: describe the grant as a denial would
        var description = rule.Describe();

        // Assert: both the resolved location and the access level appear
        Assert.Contains(rule.Root!, description, StringComparison.Ordinal);
        Assert.Contains("(read-only)", description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an unrestricted grant describes itself as "anywhere" with its level.
    /// </summary>
    [Fact]
    public void PathRule_Describe_UnrestrictedGrant_SaysAnywhere()
    {
        // Arrange: an unrestricted read-write grant
        var rule = PathRule.Unrestricted(AccessLevel.ReadWrite);

        // Act: describe the grant as a denial would
        var description = rule.Describe();

        // Assert: an unrestricted grant names no location and states its level
        Assert.Equal("anywhere (read-write)", description);
    }
}
