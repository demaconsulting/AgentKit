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
    ///     Gets a value indicating whether this platform can create a link whose recorded
    ///     target is itself a path through another link.
    /// </summary>
    /// <remarks>
    ///     Consulted by xUnit as a skip condition. Windows cannot express that arrangement
    ///     unprivileged: a junction's target is normalized when the junction is created, and a
    ///     symbolic link — which would preserve the spelling — requires a privilege an
    ///     unelevated session does not hold. The condition is written as "not Windows" rather
    ///     than as a probe so that a platform which silently lost symbolic-link support would
    ///     fail the test rather than quietly skip it.
    /// </remarks>
    public static bool SupportsLinkTargetsSpelledThroughLinks => !OperatingSystem.IsWindows();

    /// <summary>
    ///     Gets a value indicating whether this platform can express a link whose kind the
    ///     platform itself does not decode.
    /// </summary>
    /// <remarks>
    ///     Consulted by xUnit as a skip condition. Only Windows has reparse points carrying a
    ///     tag nothing on the system can interpret; every link a POSIX file system can express
    ///     is a symbolic link, which is always decoded, so there is no such case to create
    ///     there. The condition is written as "Windows" rather than as a probe so that a
    ///     Windows run which could not create the arrangement fails rather than quietly skips.
    /// </remarks>
    public static bool SupportsUndecodableLinks => OperatingSystem.IsWindows();

    /// <summary>
    ///     Proves that a path component marked as a link whose target the platform declines to
    ///     report is refused rather than carried through unresolved.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Regression guard for a fail-open. "Not a link" and "a link whose target I cannot
    ///     read" both report no target, so a resolver that consults the target alone treats the
    ///     second as the first and appends the component unchanged. The returned location then
    ///     names the link rather than what the link reaches, and a containment check over it —
    ///     which is promised a real location and deliberately does not resolve — judges the
    ///     wrong location. Every other way this resolution can fail already denies; this was
    ///     the one route that silently allowed.
    ///     </para>
    ///     <para>
    ///     The assertion is on the exception, not on a resolved value, because there is no
    ///     correct value to return: the target is genuinely unknown, and refusing is the
    ///     fail-safe reading the class documents.
    ///     </para>
    /// </remarks>
    [Fact(
        Skip = "Windows-only: a reparse point carrying a tag the platform does not decode has " +
               "no equivalent on POSIX platforms, where every link is a symbolic link and is " +
               "always decoded.",
        SkipUnless = nameof(SupportsUndecodableLinks))]
    public void RealPathResolver_Resolve_ComponentIsUndecodableLink_ThrowsIOException()
    {
        // Arrange: an entry inside the root marked as a link whose kind nothing can decode
        using var fixture = new ReparsePointFixture();
        var undecodable = fixture.CreateUndecodableReparsePoint("mount");
        var requested = Path.Combine(undecodable, "secret.txt");

        // Act & Assert: the component is refused rather than appended unchanged, and the
        // message names the component that could not be resolved
        var exception = Assert.Throws<IOException>(() => RealPathResolver.Resolve(requested));
        Assert.Contains(undecodable, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an entry that is itself a link whose target the platform declines to
    ///     report is refused, not reported as its own location.
    /// </summary>
    /// <remarks>
    ///     The leaf case of the scenario above. It is stated separately because the leaf is the
    ///     component a naive resolver would be most likely to special-case, and because a
    ///     resolver that returned the entry's own path here would report a location that is not
    ///     real while looking entirely contained.
    /// </remarks>
    [Fact(
        Skip = "Windows-only: a reparse point carrying a tag the platform does not decode has " +
               "no equivalent on POSIX platforms, where every link is a symbolic link and is " +
               "always decoded.",
        SkipUnless = nameof(SupportsUndecodableLinks))]
    public void RealPathResolver_Resolve_UndecodableLinkItself_ThrowsIOException()
    {
        // Arrange: the link itself, with nothing requested beneath it
        using var fixture = new ReparsePointFixture();
        var undecodable = fixture.CreateUndecodableReparsePoint("mount");

        // Act & Assert: refused rather than reported as its own real location
        Assert.Throws<IOException>(() => RealPathResolver.Resolve(undecodable));
    }

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
        var textRoot = Path.GetFullPath(fixture.Root);
        var realRoot = RealPathResolver.Resolve(fixture.Root);

        // Act: resolve the leaf alone, then resolve through the full component walk
        var leafTarget = File.ResolveLinkTarget(requested, returnFinalTarget: true);
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the leaf is not itself a link, so leaf-only resolution reports nothing,
        // while the component walk still reports a location outside the root. The text-based
        // comparison is made against the root as it is spelled, because that is the value a
        // check that never resolved anything would hold; comparing the unresolved request
        // against the resolved root would test the two spellings of the temporary directory
        // instead of the escape, and would fail wherever that directory is itself linked.
        Assert.Null(leafTarget);
        Assert.True(IsBeneath(textRoot, Path.GetFullPath(requested)));
        Assert.False(IsBeneath(realRoot, resolved));
    }

    /// <summary>
    ///     Proves that a link whose target is spelled through <em>another</em> link is still
    ///     reported at its real location outside the allowed root.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Regression guard for a containment escape. The platform follows a chain of links to
    ///     its end but does not canonicalize the <em>ancestors</em> of the target it reports, so
    ///     accepting a link target verbatim leaves an unresolved link in the result. Both links
    ///     here are created <em>inside</em> the root, which is exactly what an agent holding a
    ///     read-write grant over its own workspace can do, and before the resolver re-walked
    ///     substituted targets the resolved location still read as contained while the file
    ///     read was outside.
    ///     </para>
    ///     <para>
    ///     <b>Why this scenario is POSIX-only.</b> It needs a link whose recorded target is
    ///     itself a path through another link. Windows stores a junction's target already
    ///     normalized at creation, so the second link cannot be expressed there, and a Windows
    ///     symbolic link — which does preserve the spelling — requires
    ///     <c>SeCreateSymbolicLinkPrivilege</c>, which an unelevated developer session does not
    ///     hold. The test therefore declares an explicit skip condition rather than asserting
    ///     something weaker on Windows: a test that cannot fail is worse than one that is
    ///     visibly not run, and the skip is recorded with its reason in the test results.
    ///     Linux and macOS runs supply the evidence, and the requirement names them.
    ///     </para>
    /// </remarks>
    [Fact(
        Skip = "POSIX-only: Windows normalizes a junction's target at creation, and a Windows " +
               "symbolic link needs SeCreateSymbolicLinkPrivilege, so a link target spelled " +
               "through another link cannot be created on Windows.",
        SkipUnless = nameof(SupportsLinkTargetsSpelledThroughLinks))]
    public void RealPathResolver_Resolve_LinkTargetReachedThroughAnotherLink_ReturnsRealTargetOutsideRoot()
    {
        // Arrange: a secret outside the root, and two links inside the root - the first
        // pointing out of the root, the second targeting a path spelled through the first
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(
            Path.Combine(fixture.Outside, "b"),
            "secret.txt",
            "outside-content");
        fixture.CreateDirectoryLink("a", fixture.Outside);
        var indirect = fixture.CreateDirectoryLink("y", Path.Combine(fixture.Root, "a", "b"));
        var requested = Path.Combine(indirect, "secret.txt");

        // Act: resolve the path that reaches the outside file through both links
        var resolved = RealPathResolver.Resolve(requested);

        // Assert: the reported location is the real outside file, not the contained-looking
        // path the first link's target is spelled with
        Assert.Equal("outside-content", File.ReadAllText(resolved));
        Assert.True(IsBeneath(RealPathResolver.Resolve(fixture.Outside), resolved));
        Assert.False(IsBeneath(RealPathResolver.Resolve(fixture.Root), resolved));
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
