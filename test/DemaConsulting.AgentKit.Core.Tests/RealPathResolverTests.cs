namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="RealPathResolver"/> class.
/// </summary>
/// <remarks>
///     These tests exercise a real reparse point through <see cref="ReparsePointFixture"/>
///     rather than a simulation, because the behavior under test is exactly the behavior of the
///     operating system's link resolution.
/// </remarks>
public class RealPathResolverTests
{
    /// <summary>
    ///     Proves that a file whose enclosing directory is reached through a directory link is
    ///     reported at its real location outside the allowed root.
    /// </summary>
    /// <remarks>
    ///     This is the central proof of the unit: the requested path string is contained by the
    ///     root, but the file it reaches is not.
    /// </remarks>
    [Fact]
    public void RealPathResolver_Resolve_PathBeneathDirectoryLink_ReturnsRealTargetOutsideRoot()
    {
        // Arrange: a secret outside the root, reachable through a link inside the root
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var link = fixture.CreateDirectoryLink("junction", fixture.Outside);
        var requested = Path.Combine(link, "secret.txt");

        // Act: resolve the requested path to its real location
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the real location is the outside file, not a location beneath the root
        Assert.Equal("outside-content", File.ReadAllText(resolved));
        Assert.Equal("secret.txt", Path.GetFileName(resolved));
        Assert.False(IsBeneath(RealPathResolver.Resolve(fixture.Root), resolved));
    }

    /// <summary>
    ///     Proves that resolving only the leaf of the path fails to detect the escape, which is
    ///     why the resolver walks every path component.
    /// </summary>
    /// <remarks>
    ///     Regression guard. If a future change replaces the component walk with a single
    ///     resolve of the leaf, this test fails and states the reason. It is deliberately
    ///     written as a contrast between the naive approach and the implemented one.
    /// </remarks>
    [Fact]
    public void RealPathResolver_Resolve_LeafResolutionAlone_DoesNotDetectEscape()
    {
        // Arrange: an ordinary file outside the root, reached through a link inside the root
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var link = fixture.CreateDirectoryLink("junction", fixture.Outside);
        var requested = Path.Combine(link, "secret.txt");
        var realRoot = RealPathResolver.Resolve(fixture.Root);

        // Act: resolve the leaf alone, then resolve through the full component walk
        var leafTarget = File.ResolveLinkTarget(requested, returnFinalTarget: true);
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the leaf is not itself a link, so leaf-only resolution reports nothing,
        // while the component walk still reports a location outside the root
        Assert.Null(leafTarget);
        Assert.True(IsBeneath(realRoot, Path.GetFullPath(requested)));
        Assert.False(IsBeneath(realRoot, resolved));
    }

    /// <summary>
    ///     Proves that a path that is itself a directory link is reported at the link's target.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_DirectoryLinkItself_ReturnsLinkTarget()
    {
        // Arrange: a link inside the root pointing at the outside directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "marker.txt", "outside-content");
        var link = fixture.CreateDirectoryLink("junction", fixture.Outside);

        // Act: resolve the link itself rather than something beneath it
        var resolved = RealPathResolver.Resolve(link);

        // Assert: the resolved location is the target directory outside the root
        Assert.True(Directory.Exists(resolved));
        Assert.Equal("outside-content", File.ReadAllText(Path.Combine(resolved, "marker.txt")));
        Assert.False(IsBeneath(RealPathResolver.Resolve(fixture.Root), resolved));
    }

    /// <summary>
    ///     Proves that a path that does not yet exist is still reported at the real location it
    ///     would occupy, by resolving the portion of the path that does exist.
    /// </summary>
    /// <remarks>
    ///     This is what makes a write decision possible: the file being written does not exist
    ///     yet, so containment must be judged on where it would be created.
    /// </remarks>
    [Fact]
    public void RealPathResolver_Resolve_NonExistentFileBeneathLink_ReturnsRealTargetLocation()
    {
        // Arrange: a link inside the root, and a file name that has not been created
        using var fixture = new ReparsePointFixture();
        var link = fixture.CreateDirectoryLink("junction", fixture.Outside);
        var requested = Path.Combine(link, "not-created-yet.txt");

        // Act: resolve a path whose leaf does not exist
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the location is outside the root, beneath the link's real target
        Assert.False(File.Exists(resolved));
        Assert.Equal("not-created-yet.txt", Path.GetFileName(resolved));
        Assert.True(IsBeneath(RealPathResolver.Resolve(fixture.Outside), resolved));
        Assert.False(IsBeneath(RealPathResolver.Resolve(fixture.Root), resolved));
    }

    /// <summary>
    ///     Proves that relative segments are normalized away and that a relative path is
    ///     reported as an absolute location.
    /// </summary>
    [Fact]
    public void RealPathResolver_Resolve_RelativeSegments_ReturnsNormalizedAbsolutePath()
    {
        // Arrange: a real file reached both directly and through a redundant relative detour
        using var fixture = new ReparsePointFixture();
        var direct = ReparsePointFixture.WriteFile(
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
    ///     Proves that a path containing no links is reported unchanged relative to its
    ///     enclosing directory, so resolution adds nothing where there is nothing to follow.
    /// </summary>
    /// <remarks>
    ///     The expectation is expressed relative to the resolved root because the temporary
    ///     directory itself may legitimately sit beneath a link on some platforms — notably
    ///     macOS, where the temporary directory is reached through a symbolic link. The
    ///     property under test is that no <em>further</em> redirection occurs.
    /// </remarks>
    [Fact]
    public void RealPathResolver_Resolve_PathWithNoLinks_ReturnsSamePath()
    {
        // Arrange: an ordinary file directly inside the root, with no links involved
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "plain.txt", "content");
        var realRoot = RealPathResolver.Resolve(fixture.Root);

        // Act: resolve the file
        var resolved = RealPathResolver.Resolve(file);

        // Assert: the file sits exactly where its name says it does, and resolution is stable
        Assert.Equal(Path.Combine(realRoot, "plain.txt"), resolved);
        Assert.Equal(resolved, RealPathResolver.Resolve(resolved));
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
    ///     Determines whether a real location lies at or beneath a real root.
    /// </summary>
    /// <remarks>
    ///     Duplicated deliberately in the test rather than borrowed from the production
    ///     containment code, so that a defect in containment cannot mask itself by also
    ///     corrupting the expectation.
    /// </remarks>
    /// <param name="root">The real root location.</param>
    /// <param name="candidate">The real candidate location.</param>
    /// <returns><see langword="true"/> when the candidate is at or beneath the root.</returns>
    private static bool IsBeneath(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(candidate, root, comparison) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
