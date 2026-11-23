namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="RealPathResolver"/> class.
/// </summary>
/// <remarks>
///     <see cref="RealPathResolver.Resolve"/> is lexical: it makes a path absolute and collapses
///     relative segments, and it neither follows nor detects links.
///     <see cref="RealPathResolver.ResolveLinks"/> follows every link on the path. The scenarios
///     below state both contracts and the rejection of a path no caller should have supplied; the
///     link scenarios use a genuine junction or symbolic link.
/// </remarks>
public class RealPathResolverTests
{
    /// <summary>
    ///     Proves that relative segments are normalized away and that a relative path is
    ///     reported as an absolute location.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_RelativeSegments_ReturnsNormalizedAbsolutePath()
    {
        // Arrange: a real file reached both directly and through a redundant relative detour
        using var fixture = new TempDirectoryFixture();
        var direct = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "sub"),
            "file.txt",
            "content");
        var detour = Path.Combine(fixture.Root, "sub", "..", "sub", "file.txt");
        var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), direct);

        // Act: resolve the detour and the relative spelling of the same file
        var resolvedDetour = RealPathResolver.Resolve(detour);
        var resolvedRelative = RealPathResolver.Resolve(relative);

        // Assert: both spellings produce the same absolute, normalized location
        Assert.Equal(RealPathResolver.Resolve(direct), resolvedDetour);
        Assert.Equal(RealPathResolver.Resolve(direct), resolvedRelative);
        Assert.True(Path.IsPathFullyQualified(resolvedDetour));
        Assert.DoesNotContain("..", resolvedDetour, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an already-absolute path is reported unchanged, so resolution adds
    ///     nothing where there is nothing to collapse.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_NormalizedPath_ReturnsSamePath()
    {
        // Arrange: an ordinary file directly inside the root
        using var fixture = new TempDirectoryFixture();
        var file = TempDirectoryFixture.WriteFile(fixture.Root, "plain.txt", "content");
        var realRoot = RealPathResolver.Resolve(fixture.Root);

        // Act: resolve the file
        var resolved = RealPathResolver.Resolve(file);

        // Assert: the file sits exactly where its name says it does, and resolution is stable
        Assert.Equal(Path.Combine(realRoot, "plain.txt"), resolved);
        Assert.Equal(resolved, RealPathResolver.Resolve(resolved));
    }

    /// <summary>
    ///     Proves that a path that does not yet exist is still reported as a location, which is
    ///     what makes a write decision possible before the file is created.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_NonExistentPath_ReturnsLocationItWouldOccupy()
    {
        // Arrange: a file name beneath the root that has not been created
        using var fixture = new TempDirectoryFixture();
        var requested = Path.Combine(fixture.Root, "sub", "not-created-yet.txt");

        // Act: resolve a path whose leaf does not exist
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the location is reported, beneath the root, without the file existing
        Assert.False(File.Exists(resolved));
        Assert.Equal("not-created-yet.txt", Path.GetFileName(resolved));
        Assert.Equal(
            Path.Combine(RealPathResolver.Resolve(fixture.Root), "sub", "not-created-yet.txt"),
            resolved);
    }

    /// <summary>
    ///     Proves that a null path is rejected as a programming error.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_NullPath_ThrowsArgumentNullException()
    {
        // Act & Assert: a missing path is a caller defect, not a resolvable location
        Assert.Throws<ArgumentNullException>(() => RealPathResolver.Resolve(null!));
    }

    /// <summary>
    ///     Proves that an empty path is rejected as a programming error.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_EmptyPath_ThrowsArgumentException()
    {
        // Act & Assert: the empty-string boundary is distinct from the null case
        Assert.Throws<ArgumentException>(() => RealPathResolver.Resolve(string.Empty));
    }

    /// <summary>
    ///     Proves that a path reached through a directory link resolves to the location the link
    ///     leads to.
    /// </summary>
    [Fact]
    public void RealPathResolver_ResolveLinks_LinkedDirectory_ReturnsTargetLocation()
    {
        // Arrange: a junction (Windows) or symbolic link (elsewhere) inside the root, pointing
        // at a file in the sibling directory outside it
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "jn"), fixture.Outside);

        // Act: resolve the file as spelled through the link
        var resolved = RealPathResolver.ResolveLinks(Path.Combine(fixture.Root, "jn", "secret.txt"));

        // Assert: the result names the file where it really is, not where it is spelled
        Assert.Equal(
            Path.Combine(RealPathResolver.ResolveLinks(fixture.Outside), "secret.txt"),
            resolved);
    }

    /// <summary>
    ///     Proves that a file not yet created beneath a linked folder resolves to the folder's
    ///     linked location followed by the file's name, which is what a write decision needs.
    /// </summary>
    [Fact]
    public void RealPathResolver_ResolveLinks_NonExistentFileUnderLinkedParent_ReturnsTargetParentAndName()
    {
        // Arrange: a link inside the root pointing outside it, and a name that does not exist
        using var fixture = new TempDirectoryFixture();
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "jn"), fixture.Outside);
        var requested = Path.Combine(fixture.Root, "jn", "not-created-yet.png");

        // Act: resolve the destination that would be created
        var resolved = RealPathResolver.ResolveLinks(requested);

        // Assert: the linked folder's real location, followed by the name as spelled
        Assert.False(File.Exists(resolved));
        Assert.Equal(
            Path.Combine(RealPathResolver.ResolveLinks(fixture.Outside), "not-created-yet.png"),
            resolved);
    }

    /// <summary>
    ///     Proves that a path with no link of its own resolves beneath its folder's resolved
    ///     location, and that resolving the result again changes nothing.
    /// </summary>
    /// <remarks>
    ///     The expected value is built from the resolved root rather than the raw temporary path,
    ///     because on macOS the temporary folder itself lies beneath the <c>/var</c> link.
    /// </remarks>
    [Fact]
    public void RealPathResolver_ResolveLinks_NoLinks_IsIdempotent()
    {
        // Arrange: an ordinary file directly inside the root
        using var fixture = new TempDirectoryFixture();
        var file = TempDirectoryFixture.WriteFile(fixture.Root, "plain.txt", "content");

        // Act: resolve it, then resolve the result
        var resolved = RealPathResolver.ResolveLinks(file);
        var again = RealPathResolver.ResolveLinks(resolved);

        // Assert: the file sits beneath its folder's resolved location, and resolution is stable
        Assert.Equal(Path.Combine(RealPathResolver.ResolveLinks(fixture.Root), "plain.txt"), resolved);
        Assert.Equal(resolved, again);
    }

    /// <summary>
    ///     Proves that a null or empty path is rejected as a programming error.
    /// </summary>
    [Fact]
    public void RealPathResolver_ResolveLinks_NullOrEmpty_Throws()
    {
        // Act & Assert: a missing path is a caller defect, not a resolvable location
        Assert.Throws<ArgumentNullException>(() => RealPathResolver.ResolveLinks(null!));
        Assert.Throws<ArgumentException>(() => RealPathResolver.ResolveLinks(string.Empty));
    }
}
