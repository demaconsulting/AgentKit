namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="PathPolicy"/> class.
/// </summary>
/// <remarks>
///     <para>
///     A policy separates two orthogonal ideas: the single <see cref="PathPolicy.WorkingDirectory"/>
///     a relative path anchors to, and zero-or-more <see cref="PathPolicy.Grants"/> that permit
///     locations. These tests state paths the way a model states them — a bare file name, a nested
///     relative name, a placeholder, and no path at all — because those spellings are how a request
///     actually arrives. A suite that only ever passed fixture-absolute paths from a granted root let
///     the library ship unusable by a real agent while 768 tests and four reviews passed; the
///     scenarios below deliberately send the bare relative forms a model sends.
///     </para>
///     <para>
///     Where the scenario is about containment, request paths are built from a grant's resolved root
///     so the tests describe what a caller holds after resolution, and so a host whose temporary
///     directory is itself reached through a link does not perturb the expectations.
///     </para>
/// </remarks>
public class PathPolicyTests
{
    // ---------------------------------------------------------------------------------------------
    // Containment (regression): the reparse-point walk and deny logic are unchanged.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a file reached through a link out of the permitted location is refused.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_FileBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a policy confined to the root, and a secret reachable only through a link
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WorkingDirectory, "junction", "secret.txt");

        // Act: ask the policy to resolve the escaping path
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: the request is refused and no location is handed back
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that a path which passes a naive string prefix check is still refused once its
    ///     real location is known.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_NaivePrefixCheckWouldPass_StillDenied()
    {
        // Arrange: a request whose text is contained by the root but whose target is not
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WorkingDirectory, "junction", "secret.txt");

        // Act: evaluate the naive check and the real policy decision
        var naiveCheckPasses = requested.StartsWith(
            policy.WorkingDirectory + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
        var permitted = policy.TryResolveRead(requested, out _, out _);

        // Assert: the naive check is satisfied and the policy refuses anyway
        Assert.True(naiveCheckPasses);
        Assert.False(permitted);
    }

    /// <summary>
    ///     Gets a value indicating whether this platform can express a link whose kind the
    ///     platform itself does not decode.
    /// </summary>
    /// <remarks>
    ///     Consulted by xUnit as a skip condition; see the matching condition on
    ///     <see cref="RealPathResolverTests"/> for why only Windows has such a case.
    /// </remarks>
    public static bool SupportsUndecodableLinks => OperatingSystem.IsWindows();

    /// <summary>
    ///     Proves that a path passing through an entry marked as a link whose target the platform
    ///     declines to report is refused rather than permitted at the link's own location.
    /// </summary>
    /// <remarks>
    ///     The end-to-end form of the resolver's fail-safe refusal. Before the resolver
    ///     distinguished "not a link" from "a link I cannot resolve", the unresolved component was
    ///     carried through and this request was <em>permitted</em>, with the link's own path handed
    ///     back as though it were a real location.
    /// </remarks>
    [Fact(
        Skip = "Windows-only: a reparse point carrying a tag the platform does not decode has " +
               "no equivalent on POSIX platforms, where every link is a symbolic link and is " +
               "always decoded.",
        SkipUnless = nameof(SupportsUndecodableLinks))]
    public void PathPolicy_TryResolveRead_UndecodableLinkInsideGrant_ReturnsDenial()
    {
        // Arrange: an entry inside the granted root marked as a link nothing can decode
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        fixture.CreateUndecodableReparsePoint("mount");
        var requested = Path.Combine(policy.WorkingDirectory, "mount", "secret.txt");

        // Act: ask the policy for a path that passes through it
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denial);

        // Assert: refused, with no location handed back and the unknown stated as the reason
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
        Assert.Contains(
            "could not be resolved to a real location",
            denial,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a relative path climbing out of the working directory is refused.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativeParentTraversal_ReturnsDenial()
    {
        // Arrange: a secret in the sibling directory outside the working directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: climb out of the working directory with a relative path
        var permitted = policy.TryResolveRead("../outside/secret.txt", out var realPath, out var denial);

        // Assert: refused, with no location handed back
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
    }

    /// <summary>
    ///     Proves that a relative path reaching outside through a link is refused exactly as an
    ///     absolute one is.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativePathBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a secret outside the working directory, reachable through a link inside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask for the escaping path relatively, as a model would after a listing
        var permitted = policy.TryResolveRead("junction/secret.txt", out var realPath, out var denial);

        // Assert: refused, exactly as the absolute spelling of the same request is
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
    }

    /// <summary>
    ///     Proves that a permitted path is reported together with its real location.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_PermittedPath_ReturnsRealPath()
    {
        // Arrange: a policy confined to the root, and an ordinary file inside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WorkingDirectory, "notes.txt");

        // Act: resolve a contained path
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: the caller receives the real location and no denial
        Assert.True(permitted);
        Assert.Null(denialMessage);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a refused path produces a returned denial rather than an exception.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_DeniedPath_ReturnsFalseWithoutThrowing()
    {
        // Arrange: a policy confined to the root, and a location outside it
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: resolve a path outside the permitted location
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: refusal is reported by the return value, with a reason
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotEmpty(denialMessage!);
    }

    /// <summary>
    ///     Proves that a path whose real location cannot be determined is refused rather than allowed
    ///     to throw.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_MalformedPath_ReturnsDenial()
    {
        // Arrange: a policy confined to the root, and a path no platform can resolve
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        const string malformed = "a\0b";

        // Act: resolve the malformed path
        var permitted = policy.TryResolveRead(malformed, out var realPath, out var denialMessage);

        // Assert: the request is refused, with no exception escaping
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    // ---------------------------------------------------------------------------------------------
    // Working directory + grant combinations (the agent's viewpoint).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a working directory granted read-write reads, writes, and lists relative
    ///     paths, and reports them in the relative dialect.
    /// </summary>
    [Fact]
    public void PathPolicy_WorkingDirectoryGrantedReadWrite_RelativeRequestsSucceed()
    {
        // Arrange: the one common shape — the working directory is also a read-write grant
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: read a bare name, write a bare name, and ask whether output would be relative
        var read = policy.TryResolveRead("notes.txt", out var readPath, out _);
        var write = policy.TryResolveWrite("new-file.txt", out var writePath, out _);
        var relative = policy.EmitRelative(readPath!, "notes.txt");

        // Assert: both succeed beneath the anchor, and the dialect is relative
        Assert.True(read);
        Assert.Equal("inside-content", File.ReadAllText(readPath!));
        Assert.True(write);
        Assert.Equal(Path.Combine(policy.WorkingDirectory, "new-file.txt"), writePath);
        Assert.True(relative);
    }

    /// <summary>
    ///     Proves that a working directory granted read-only reads and lists relative paths but
    ///     refuses a relative write, enumerating the location as read-only.
    /// </summary>
    [Fact]
    public void PathPolicy_WorkingDirectoryGrantedReadOnly_RelativeWriteDenied()
    {
        // Arrange: the working directory is granted, but read-only
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: read a bare name, then attempt to write one
        var read = policy.TryResolveRead("notes.txt", out _, out _);
        var write = policy.TryResolveWrite("notes.txt", out var writePath, out var denial);

        // Assert: reads succeed; writes are refused with the read-only location enumerated
        Assert.True(read);
        Assert.False(write);
        Assert.Null(writePath);
        Assert.Contains("(read-only)", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a working directory granted nothing resolves a relative path against itself
    ///     and then denies it, stating that no location is permitted.
    /// </summary>
    /// <remarks>
    ///     This is the app-folder anchor case: the working directory is a coherent relative anchor
    ///     even though it grants nothing. A bare name resolves against it correctly and is then
    ///     correctly denied, and the denial names what is permitted — here, nothing.
    /// </remarks>
    [Fact]
    public void PathPolicy_WorkingDirectoryGrantedNothing_RelativeReadDenied_NamesNoLocations()
    {
        // Arrange: an anchor with an empty grant set
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = new PathPolicy(fixture.Root, []);

        // Act: ask for a file that really is beneath the anchor
        var permitted = policy.TryResolveRead("notes.txt", out var realPath, out var denial);

        // Assert: refused, and the denial says plainly that nothing is permitted
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.Contains("No locations are permitted.", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an ungranted working directory with grants elsewhere denies a relative path
    ///     and enumerates the granted locations.
    /// </summary>
    [Fact]
    public void PathPolicy_UngrantedWorkingDirectory_RelativeReadDenied_EnumeratesElsewhere()
    {
        // Arrange: anchor at Root (ungranted); grant Outside instead
        using var fixture = new ReparsePointFixture();
        var outsideReal = RealPathResolver.Resolve(fixture.Outside);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Outside)]);

        // Act: a bare name resolves under the ungranted anchor and is denied
        var permitted = policy.TryResolveRead("notes.txt", out _, out var denial);

        // Assert: the denial enumerates the location that IS permitted
        Assert.False(permitted);
        Assert.Contains(outsideReal, denial, StringComparison.Ordinal);
        Assert.Contains("(read-only)", denial, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Multiple grants and a cross-location task.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that two grants of different levels each govern their own location: a cross-location
    ///     task reads the read-only location and writes the read-write one.
    /// </summary>
    [Fact]
    public void PathPolicy_TwoGrants_CrossLocationTask_ReadOnlyReadsAndReadWriteWrites()
    {
        // Arrange: work is read-only, session is read-write, anchored at work
        using var fixture = new ReparsePointFixture();
        var work = Path.Combine(fixture.Root, "work");
        var session = Path.Combine(fixture.Root, "session");
        ReparsePointFixture.WriteFile(work, "input.txt", "source");
        Directory.CreateDirectory(session);
        var policy = new PathPolicy(
            work,
            [PathRule.ReadOnly(work), PathRule.ReadWrite(session)]);

        var workFile = Path.Combine(RealPathResolver.Resolve(work), "input.txt");
        var sessionFile = Path.Combine(RealPathResolver.Resolve(session), "summary.txt");

        // Act: read from work, write to session, and try to write into read-only work
        var readWork = policy.TryResolveRead(workFile, out _, out _);
        var writeSession = policy.TryResolveWrite(sessionFile, out _, out _);
        var writeWork = policy.TryResolveWrite(workFile, out _, out var workDenial);

        // Assert: read-only reads, read-write writes, and a write into read-only work is refused
        Assert.True(readWork);
        Assert.True(writeSession);
        Assert.False(writeWork);
        Assert.Contains("(read-only)", workDenial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a location readable through a read-only grant is not thereby writable, while a
    ///     wider read grant still authorizes the read.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveWrite_ReadableButNotWritablePath_ReturnsDenial()
    {
        // Arrange: reads are unrestricted, writes confined to the root
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.Unrestricted(AccessLevel.ReadOnly), PathRule.ReadWrite(fixture.Root)]);
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "readable.txt", "content");

        // Act: read and then attempt to write the same outside location
        var readPermitted = policy.TryResolveRead(requested, out _, out _);
        var writePermitted = policy.TryResolveWrite(requested, out _, out var denialMessage);

        // Assert: readable does not imply writable
        Assert.True(readPermitted);
        Assert.False(writePermitted);
        Assert.NotNull(denialMessage);
    }

    // ---------------------------------------------------------------------------------------------
    // Constructor guards.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a missing working directory is a programming error, not a guessed default.
    /// </summary>
    /// <param name="workingDirectory">The absent spelling of the working directory.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void PathPolicy_Constructor_MissingWorkingDirectory_ThrowsArgumentException(
        string? workingDirectory)
    {
        // Act & Assert: the library will not anchor relative paths at a value nobody supplied
        Assert.ThrowsAny<ArgumentException>(
            () => new PathPolicy(workingDirectory!, [PathRule.Unrestricted(AccessLevel.ReadWrite)]));
    }

    /// <summary>
    ///     Proves that a null grant collection is rejected.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NullGrants_ThrowsArgumentNullException()
    {
        // Act & Assert: a null grant set is a programming error
        using var fixture = new ReparsePointFixture();
        Assert.Throws<ArgumentNullException>(() => new PathPolicy(fixture.Root, null!));
    }

    /// <summary>
    ///     Proves that a null grant within the collection is rejected.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NullGrantEntry_ThrowsArgumentNullException()
    {
        // Act & Assert: a half-built grant set cannot be used
        using var fixture = new ReparsePointFixture();
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(fixture.Root, [null!]));
    }

    /// <summary>
    ///     Proves that an empty grant set is a valid, fully-confined policy that permits nothing.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_EmptyGrants_IsValidAndPermitsNothing()
    {
        // Arrange & Act: a policy that grants nothing
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, []);

        // Assert: it constructs, exposes its anchor, and refuses even its own anchor
        Assert.Empty(policy.Grants);
        Assert.False(policy.TryResolveRead(null, out _, out _));
    }

    /// <summary>
    ///     Proves that a policy created without explicit ceilings carries the library's documented
    ///     ones by reference.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NoLimits_UsesDefaultLimits()
    {
        // Arrange & Act: construct a policy without stating any ceilings
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Assert: the shared default instance, not a copy of it
        Assert.Same(ToolLimits.Default, policy.Limits);
    }

    /// <summary>
    ///     Proves that a policy exposes the ceilings the host supplied.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_CustomLimits_ExposesSuppliedLimits()
    {
        // Arrange: a host that tightens the binary-content ceiling
        using var fixture = new ReparsePointFixture();
        var limits = new ToolLimits(maxBinaryBytes: 1024);

        // Act: construct a policy carrying those ceilings
        var policy = new PathPolicy(
            fixture.Root, [PathRule.Unrestricted(AccessLevel.ReadWrite)], limits);

        // Assert: the tools this policy governs observe the host's budget
        Assert.Same(limits, policy.Limits);
    }

    /// <summary>
    ///     Proves that a policy cannot be created with a missing set of ceilings.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NullLimits_ThrowsArgumentNullException()
    {
        // Act & Assert: ceilings are required whenever they are stated explicitly
        using var fixture = new ReparsePointFixture();
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(fixture.Root, [PathRule.Unrestricted(AccessLevel.ReadWrite)], null!));
    }

    /// <summary>
    ///     Proves that a working directory reached through a link is held at its real location.
    /// </summary>
    [Fact]
    public void PathPolicy_WorkingDirectory_ReachedThroughLink_IsReportedAsItsRealLocation()
    {
        // Arrange: a link inside the fixture pointing at a sibling directory
        using var fixture = new ReparsePointFixture();
        var link = fixture.CreateDirectoryLink("alias", fixture.Outside);

        // Act: name the link as the working directory
        var policy = new PathPolicy(link, [PathRule.ReadWrite(link)]);

        // Assert: the real location is what the policy holds, not the link's own path
        Assert.Equal(RealPathResolver.Resolve(fixture.Outside), policy.WorkingDirectory);
    }

    // ---------------------------------------------------------------------------------------------
    // Relative addressing (the paths a model actually sends).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a name prefixed with the current-directory token resolves beneath the anchor.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_DotSlashFileName_ResolvesBeneathTheAnchor()
    {
        // Arrange: a working directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask for the file with the leading current-directory token a model often adds
        var permitted = policy.TryResolveRead("./notes.txt", out var realPath, out _);

        // Assert: the same file the bare name reaches
        Assert.True(permitted);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a nested relative path resolves beneath the anchor.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_NestedRelativePath_ResolvesBeneathTheAnchor()
    {
        // Arrange: a file one level below the working directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "nested");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask for it the way a model writes a nested path
        var permitted = policy.TryResolveRead("sub/child.txt", out var realPath, out _);

        // Assert: the nested file, reached from the working directory
        Assert.True(permitted);
        Assert.Equal("nested", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a relative path is not measured from the process working directory.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativePath_IsNotResolvedAgainstTheProcessDirectory()
    {
        // Arrange: a working directory that is deliberately not the process working directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask for the file by name alone
        policy.TryResolveRead("notes.txt", out var realPath, out _);

        // Assert: the working directory supplied the location, not wherever this process started
        Assert.NotNull(realPath);
        Assert.StartsWith(policy.WorkingDirectory, realPath, StringComparison.Ordinal);
        Assert.NotEqual(Path.Combine(Environment.CurrentDirectory, "notes.txt"), realPath);
    }

    /// <summary>
    ///     Proves that an absolute path inside the working directory remains expressible.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_AbsolutePathInsideRoot_ReturnsRealPath()
    {
        // Arrange: a working directory holding one file, addressed absolutely
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WorkingDirectory, "notes.txt");

        // Act: ask for the file by its absolute location
        var permitted = policy.TryResolveRead(requested, out var realPath, out _);

        // Assert: permitted, and the same file the bare name reaches
        Assert.True(permitted);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that an absolute path outside the working directory is refused.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_AbsolutePathOutsideRoot_ReturnsDenial()
    {
        // Arrange: a file in the sibling directory outside the working directory
        using var fixture = new ReparsePointFixture();
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask for it by its absolute location
        var permitted = policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: containment is unchanged by the anchor
        Assert.False(permitted);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that an omitted path denotes the working directory itself rather than raising an
    ///     error.
    /// </summary>
    /// <param name="path">The spelling of "no path" the caller supplied.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PathPolicy_TryResolveRead_OmittedPath_ResolvesToTheAnchor(string? path)
    {
        // Arrange: a working directory a model has not yet named
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: resolve the omitted request
        var permitted = policy.TryResolveRead(path, out var realPath, out _);

        // Assert: the working directory itself, with no exception escaping
        Assert.True(permitted);
        Assert.Equal(policy.WorkingDirectory, realPath);
    }

    /// <summary>
    ///     Proves that the literal words a model sends for "no path" denote the working directory.
    /// </summary>
    /// <param name="path">The placeholder spelling the caller supplied.</param>
    [Theory]
    [InlineData("None")]
    [InlineData("null")]
    [InlineData("NONE")]
    public void PathPolicy_TryResolveRead_PlaceholderPath_ResolvesToTheAnchor(string path)
    {
        // Arrange: a working directory a model has not yet named
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: resolve the placeholder request
        var permitted = policy.TryResolveRead(path, out var realPath, out _);

        // Assert: the working directory itself, exactly as an omitted argument produces
        Assert.True(permitted);
        Assert.Equal(policy.WorkingDirectory, realPath);
    }

    /// <summary>
    ///     Proves that a bare file name resolves beneath the anchor for writing too.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveWrite_BareFileName_ResolvesBeneathTheAnchor()
    {
        // Arrange: a working directory and a file that does not exist yet
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: ask to write the file by name alone
        var permitted = policy.TryResolveWrite("new-file.txt", out var realPath, out _);

        // Assert: the location beneath the working directory the caller may create
        Assert.True(permitted);
        Assert.Equal(Path.Combine(policy.WorkingDirectory, "new-file.txt"), realPath);
    }

    // ---------------------------------------------------------------------------------------------
    // Dialect mirroring (EmitRelative) and discovery roots.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves the dialect rule: relative-eligible in and within the granted anchor emits relative;
    ///     absolute in, or a result outside the anchor, or an ungranted anchor emits absolute.
    /// </summary>
    [Fact]
    public void PathPolicy_EmitRelative_MirrorsTheCallerAndTheResultLocation()
    {
        // Arrange: a granted anchor and a result inside it, plus one outside it
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var inside = Path.Combine(policy.WorkingDirectory, "notes.txt");
        var outside = RealPathResolver.Resolve(Path.Combine(fixture.Outside, "x.txt"));

        // Act & Assert: relative in, within anchor -> relative
        Assert.True(policy.EmitRelative(inside, "notes.txt"));

        // Absolute in -> absolute, even for a result within the anchor
        Assert.False(policy.EmitRelative(inside, inside));

        // Discovery (no path) establishes the dialect; a granted anchor makes it relative
        Assert.True(policy.EmitRelative(policy.WorkingDirectory, null));

        // A result outside the anchor cannot be named relatively
        Assert.False(policy.EmitRelative(outside, "notes.txt"));
    }

    /// <summary>
    ///     Proves that an ungranted working directory establishes the absolute dialect even for a
    ///     discovery request.
    /// </summary>
    [Fact]
    public void PathPolicy_EmitRelative_UngrantedAnchor_IsAlwaysAbsolute()
    {
        // Arrange: the anchor is not among the grants
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Outside)]);

        // Act & Assert: nothing is relative-eligible when the anchor is ungranted
        Assert.False(policy.WorkingDirectoryIsGranted);
        Assert.False(policy.EmitRelative(policy.WorkingDirectory, null));
    }

    /// <summary>
    ///     Proves that discovery roots list one location per grant, substituting the working directory
    ///     for an unrestricted grant.
    /// </summary>
    [Fact]
    public void PathPolicy_DiscoveryRoots_ListOneLocationPerGrant()
    {
        // Arrange: a rooted grant and an unrestricted grant
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: enumerate the discovery roots
        var roots = policy.DiscoveryRoots();

        // Assert: the rooted grant's location, and the anchor standing in for the unrestricted one
        Assert.Contains(RealPathResolver.Resolve(fixture.Root), roots);
        Assert.Contains(policy.WorkingDirectory, roots);
    }

    // ---------------------------------------------------------------------------------------------
    // The last-segment grant alias.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a bare segment equal to one grant's final folder name resolves to that grant.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_LastSegmentAlias_ResolvesToTheGrant()
    {
        // Arrange: anchor at Root (ungranted); grant Outside, whose final segment is "outside"
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "file.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Outside)]);

        // Act: name the grant by its final folder name alone
        var permitted = policy.TryResolveRead("outside", out var realPath, out _);

        // Assert: the alias resolves to the granted location, not to Root\outside
        Assert.True(permitted);
        Assert.Equal(RealPathResolver.Resolve(fixture.Outside), realPath);
    }

    /// <summary>
    ///     Proves that a bare segment matching two grants is not aliased, and the denial enumerates
    ///     both same-named locations.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_AmbiguousAlias_IsNotAliased_EnumeratesBoth()
    {
        // Arrange: two grants whose final segment is the same word "shared", neither of which is
        // the anchor-relative interpretation of the input (so the fallthrough is genuinely denied)
        using var fixture = new ReparsePointFixture();
        var firstShared = Path.Combine(fixture.Outside, "a", "shared");
        var secondShared = Path.Combine(fixture.Outside, "b", "shared");
        Directory.CreateDirectory(firstShared);
        Directory.CreateDirectory(secondShared);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(firstShared), PathRule.ReadOnly(secondShared)]);

        // Act: name the ambiguous segment; it must not silently pick one
        var permitted = policy.TryResolveRead("shared", out var realPath, out var denial);

        // Assert: denied (it resolved under the anchor, Root\shared is not granted itself), and
        // both same-named locations are named so the model can address one absolutely
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.Contains(RealPathResolver.Resolve(firstShared), denial, StringComparison.Ordinal);
        Assert.Contains(RealPathResolver.Resolve(secondShared), denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a bare segment naming a real subfolder of the working directory resolves to
    ///     that subfolder, even when a different granted location has the same final folder name.
    ///     The working-directory interpretation is the documented mechanism and wins; the alias is
    ///     only a fallback.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_WorkingDirectorySubfolderShadowsSameNamedGrant()
    {
        // Arrange: the working directory contains a real "docs" subfolder, AND a second granted
        // location also ends in "docs". The model asks for "docs" meaning its own subfolder.
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "docs"), "a.txt", "inside");
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Outside, "docs"), "b.txt", "elsewhere");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadOnly(Path.Combine(fixture.Outside, "docs"))]);

        // Act: name the bare segment "docs"
        var permitted = policy.TryResolveRead("docs", out var realPath, out _);

        // Assert: the working-directory subfolder wins over the same-named grant
        Assert.True(permitted);
        Assert.Equal(RealPathResolver.Resolve(Path.Combine(fixture.Root, "docs")), realPath);
    }

    /// <summary>
    ///     Proves that the alias still fires as a fallback when the working-directory interpretation
    ///     of a bare segment does not name an existing path (the original ergonomic case, e.g.
    ///     <c>file_list("work")</c> with no <c>work</c> subfolder under the working directory).
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_BareSegmentNotUnderWorkingDirectory_FallsBackToAlias()
    {
        // Arrange: the working directory has no "work" subfolder; a granted location ends in "work"
        using var fixture = new ReparsePointFixture();
        var grantedWork = Path.Combine(fixture.Outside, "work");
        ReparsePointFixture.WriteFile(grantedWork, "input.txt", "source");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(grantedWork)]);

        // Act: name the grant by its final folder name alone
        var permitted = policy.TryResolveRead("work", out var realPath, out _);

        // Assert: with no Root\work to interpret against, the alias resolves to the granted location
        Assert.True(permitted);
        Assert.Equal(RealPathResolver.Resolve(grantedWork), realPath);
    }

    /// <summary>
    ///     Proves that a bare segment naming a real subfolder of the working directory that matches
    ///     no grant uses the working-directory interpretation rather than any alias, and is judged
    ///     against the containing grant.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_BareSegmentUnderWorkingDirectory_UsesWorkingDirectoryNotAlias()
    {
        // Arrange: the working directory (itself granted) contains a real "notes" subfolder; no
        // grant ends in "notes"
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "notes"), "n.txt", "inside");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: name the bare segment "notes"
        var permitted = policy.TryResolveRead("notes", out var realPath, out _);

        // Assert: it resolves to Root\notes, within the Root grant
        Assert.True(permitted);
        Assert.Equal(RealPathResolver.Resolve(Path.Combine(fixture.Root, "notes")), realPath);
    }

    /// <summary>
    ///     Proves that a relative request containing a separator is never aliased even when its
    ///     final segment matches a grant: the separator disqualifies the alias and the request is
    ///     joined to the working directory.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_NonBareRelativeSameNameAsGrant_IsNotAliased()
    {
        // Arrange: a granted location ends in "shared"; the request "sub/shared" contains a
        // separator and must be joined to the working directory, not aliased
        using var fixture = new ReparsePointFixture();
        var grantedShared = Path.Combine(fixture.Outside, "shared");
        ReparsePointFixture.WriteFile(grantedShared, "s.txt", "elsewhere");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(grantedShared)]);

        // Act: a non-bare relative request whose final segment matches the grant
        var permitted = policy.TryResolveRead(
            "sub" + Path.DirectorySeparatorChar + "shared", out var realPath, out var denial);

        // Assert: it is joined to the working directory (Root\sub\shared), not aliased to the
        // granted location, and is therefore denied
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
    }

    // ---------------------------------------------------------------------------------------------
    // Denial construction (echo, interpretation, enumeration).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that a relative denial echoes the input, states the interpretation, and enumerates
    ///     the permitted locations with their access levels.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativeDenial_EchoesInterpretsAndEnumerates()
    {
        // Arrange: a granted read-only anchor, and a request that climbs out of it
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: a relative request that escapes the anchor
        policy.TryResolveRead("../outside/secret.txt", out _, out var denial);

        // Assert: (a) the input is echoed verbatim
        Assert.NotNull(denial);
        Assert.Contains("Requested: \"../outside/secret.txt\"", denial, StringComparison.Ordinal);

        // (b) the interpretation is stated because the request was relative
        Assert.Contains("Interpreted as:", denial, StringComparison.Ordinal);

        // (c) the permitted locations are enumerated with their level
        Assert.Contains(policy.WorkingDirectory, denial, StringComparison.Ordinal);
        Assert.Contains("(read-only)", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an absolute denial echoes the input and enumerates locations but does not
    ///     report an interpretation, because none occurred.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_AbsoluteDenial_HasNoInterpretationClause()
    {
        // Arrange: a granted anchor and an absolute request outside it
        using var fixture = new ReparsePointFixture();
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "x");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: an absolute request outside the anchor
        policy.TryResolveRead(requested, out _, out var denial);

        // Assert: the input is echoed, but no interpretation is reported for an absolute request
        Assert.NotNull(denial);
        Assert.Contains($"Requested: \"{requested}\"", denial, StringComparison.Ordinal);
        Assert.DoesNotContain("Interpreted as:", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a denial for a path that is spelled inside a permitted location but leads
    ///     outside it names the real location the path resolved to.
    /// </summary>
    /// <remarks>
    ///     An author who mounts data beneath a granted folder sees a request that looks contained
    ///     being refused, and without the real location the denial reads as a defect. The message
    ///     states where the path actually leads and stops there: what to do about it is the
    ///     author's decision.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_LinkEscape_DenialNamesTheRealLocation()
    {
        // Arrange: a secret outside the root, reachable through a link inside it
        using var fixture = new ReparsePointFixture();
        var secret = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WorkingDirectory, "junction", "secret.txt");

        // Act: ask for the contained-looking path
        policy.TryResolveRead(requested, out _, out var denial);

        // Assert: the denial names where the path really leads, and prescribes nothing
        Assert.NotNull(denial);
        Assert.Contains(
            $"Resolved to: {RealPathResolver.Resolve(secret)}",
            denial,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a denial for a path that leads exactly where it is spelled says nothing
    ///     about a real location, because there is nothing further to state.
    /// </summary>
    /// <remarks>
    ///     The clause exists to explain a redirection. Emitting it unconditionally would repeat
    ///     the line above it and would disclose a resolved location for requests where no
    ///     redirection occurred.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_LinkFreeDenial_DoesNotNameARealLocation()
    {
        // Arrange: a granted anchor and an ordinary absolute request outside it
        using var fixture = new ReparsePointFixture();
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "x");
        var policy = CreateRootedPolicy(RealPathResolver.Resolve(fixture.Root));

        // Act: an absolute request that involves no link at all
        policy.TryResolveRead(RealPathResolver.Resolve(requested), out _, out var denial);

        // Assert: the denial carries no resolved-location clause
        Assert.NotNull(denial);
        Assert.DoesNotContain("Resolved to:", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the worked example: <c>file_list("work")</c> under an ungranted anchor is no longer
    ///     baffling — it names the input, the interpretation, and the empty grant set.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_BareSegmentUnderEmptyGrants_ProducesTheWorkedExample()
    {
        // Arrange: an ungranted anchor and the bare word "work"
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, []);

        // Act: the model asks to list "work"
        policy.TryResolveRead("work", out _, out var denial);

        // Assert: the input, the "work/work" interpretation, and the empty grant set all appear
        Assert.NotNull(denial);
        Assert.Contains("Requested: \"work\"", denial, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(policy.WorkingDirectory, "work"), denial, StringComparison.Ordinal);
        Assert.Contains("No locations are permitted.", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an omitted request is echoed with a stand-in rather than an empty quotation.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_OmittedDenial_EchoesAStandIn()
    {
        // Arrange: an ungranted anchor so that even the anchor itself is refused
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, []);

        // Act: resolve with no path at all
        policy.TryResolveRead(null, out _, out var denial);

        // Assert: the echo names the working directory rather than quoting nothing
        Assert.NotNull(denial);
        Assert.Contains("(no path", denial, StringComparison.Ordinal);
        Assert.DoesNotContain("Interpreted as:", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a parent-traversal relative request denied for escaping the anchor reports a
    ///     normalized interpreted location, with the <c>..</c> segment collapsed rather than echoed
    ///     verbatim, so the reported location names a real place.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativeParentTraversalDenial_InterpretedPathIsNormalized()
    {
        // Arrange: a granted read-only anchor, and a request that climbs out of it
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: a relative request that escapes the anchor via a parent segment
        policy.TryResolveRead("../outside.md", out _, out var denial);

        // Assert: the interpretation is reported, and it carries no un-collapsed ".." segment
        Assert.NotNull(denial);
        Assert.Contains("Interpreted as:", denial, StringComparison.Ordinal);
        Assert.DoesNotContain("..", InterpretedLine(denial), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a <c>./</c>-prefixed relative request that still escapes is reported with both
    ///     the <c>.</c> and the <c>..</c> segments collapsed.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_DotSlashDenial_InterpretedPathIsNormalized()
    {
        // Arrange: a granted read-only anchor, and a "./"-prefixed escaping request
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: a relative request that begins with "./" and then escapes the anchor
        policy.TryResolveRead("./../outside.md", out _, out var denial);

        // Assert: the interpretation is reported, and neither "." nor ".." navigation survives
        Assert.NotNull(denial);
        Assert.Contains("Interpreted as:", denial, StringComparison.Ordinal);
        var interpreted = InterpretedLine(denial);
        Assert.DoesNotContain("..", interpreted, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar + "." + Path.DirectorySeparatorChar,
            interpreted,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a nested traversal request collapses correctly, reporting exactly the
    ///     lexically normalized location the resolver would be asked about.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_NestedTraversalDenial_InterpretedPathIsNormalized()
    {
        // Arrange: a granted read-only anchor, and a nested traversal request that escapes it
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        const string requested = "a/../../b/outside.md";
        var expected = Path.GetFullPath(Path.Combine(policy.WorkingDirectory, requested));

        // Act: a nested relative traversal request
        policy.TryResolveRead(requested, out _, out var denial);

        // Assert: the reported interpretation equals the lexically normalized location
        Assert.NotNull(denial);
        Assert.Contains("Interpreted as: " + expected, denial, StringComparison.Ordinal);
        Assert.DoesNotContain("..", InterpretedLine(denial), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the no-regression case: a plain relative request needing no normalization is still
    ///     reported as the working-directory-combined location, exactly as before the normalization
    ///     was introduced.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_PlainRelativeDenial_InterpretedPathReportedVerbatim()
    {
        // Arrange: an ungranted anchor and a plain relative name with nothing to collapse
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, []);

        // Act: the model asks for a plain relative segment
        policy.TryResolveRead("work", out _, out var denial);

        // Assert: the interpretation is the working-directory-combined path, unchanged by normalization
        Assert.NotNull(denial);
        Assert.Contains(
            "Interpreted as: " + Path.Combine(policy.WorkingDirectory, "work"),
            denial,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a relative request whose normalization would throw still yields a denial —
    ///     never an exception — and the denial still echoes the request and enumerates the permitted
    ///     locations, confirming the non-throwing fallback in the interpreted-path helper.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_InterpretedPathNormalizationThrows_StillDeniesAndDiscloses()
    {
        // Arrange: a granted read-only anchor, and a relative path whose normalization throws
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        const string malformed = "../a\0b";

        // Act: resolve a malformed relative request whose normalization would throw
        var permitted = policy.TryResolveRead(malformed, out var realPath, out var denial);

        // Assert: the request is refused with no exception escaping, and the denial still discloses
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
        Assert.Contains($"Requested: \"{malformed}\"", denial, StringComparison.Ordinal);
        Assert.Contains("Permitted locations:", denial, StringComparison.Ordinal);
        Assert.Contains("  - " + policy.WorkingDirectory + " (read-only)", denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a write aliased to a read-only grant is denied without reporting an
    ///     interpretation, because the alias branch performs no working-directory interpretation.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveWrite_AliasToReadOnlyGrant_DeniedWithoutInterpretationClause()
    {
        // Arrange: anchor at Root (nothing named "outside" beneath it); grant Outside read-only,
        // whose final segment is "outside" so the bare segment aliases to it
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Outside)]);

        // Act: a write to the bare alias, which resolves to a read-only grant
        var permitted = policy.TryResolveWrite("outside", out var realPath, out var denial);

        // Assert: denied because it is read-only, and no interpretation is reported for an alias
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denial);
        Assert.DoesNotContain("Interpreted as:", denial, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // Enumeration (unchanged single-decision filtering).
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    ///     Proves that enumeration excludes a file only reachable by leaving the permitted location.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_LinkToOutsideRoot_ExcludesEscapedFile()
    {
        // Arrange: a secret outside the root, reachable through a link inside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        ReparsePointFixture.WriteFile(fixture.Root, "inside.txt", "inside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: enumerate raw, then through the policy
        var rawFiles = Directory
            .EnumerateFiles(policy.WorkingDirectory, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToArray();
        var policyFiles = policy
            .EnumerateFiles(policy.WorkingDirectory, "*")
            .Select(Path.GetFileName)
            .ToArray();

        // Assert: raw enumeration crosses the link; the policy's enumeration does not
        Assert.Contains("secret.txt", rawFiles);
        Assert.DoesNotContain("secret.txt", policyFiles);
        Assert.Contains("inside.txt", policyFiles);
    }

    /// <summary>
    ///     Proves that enumeration lists permitted files at any depth.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_PermittedFiles_AreListed()
    {
        // Arrange: two contained files, one of them nested
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "nested"), "deep.txt", "deep");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: enumerate the permitted subtree
        var files = policy.EnumerateFiles(policy.WorkingDirectory, "*").Select(Path.GetFileName).ToArray();

        // Assert: every permitted file appears, at any depth
        Assert.Contains("top.txt", files);
        Assert.Contains("deep.txt", files);
    }

    /// <summary>
    ///     Proves that enumerating a refused directory returns nothing rather than throwing.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_DeniedDirectory_ReturnsEmpty()
    {
        // Arrange: a populated directory outside the permitted location
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: enumerate the directory outside the permitted location
        var files = policy.EnumerateFiles(fixture.Outside, "*");

        // Assert: an empty listing, with no exception
        Assert.Empty(files);
    }

    /// <summary>
    ///     Proves that an omitted directory enumerates the working directory.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_OmittedDirectory_ListsTheAnchor()
    {
        // Arrange: a working directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: enumerate with no directory supplied
        var files = policy.EnumerateFiles(null, "*").Select(Path.GetFileName).ToArray();

        // Assert: the working directory is what "no directory" means
        Assert.Contains("top.txt", files);
    }

    /// <summary>
    ///     Proves that a relative directory enumerates that directory beneath the anchor.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_RelativeDirectory_ListsThatDirectory()
    {
        // Arrange: one file in a subdirectory and one outside it but still in the workspace
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "deep.txt", "deep");
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: enumerate the subdirectory by its relative name
        var files = policy.EnumerateFiles("sub", "*").Select(Path.GetFileName).ToArray();

        // Assert: the named subdirectory, not the whole workspace
        Assert.Equal(["deep.txt"], files);
    }

    /// <summary>
    ///     Creates a policy whose working directory is also its single read-write grant — the common
    ///     shape, where one folder is both the relative anchor and the permitted location.
    /// </summary>
    /// <param name="root">The location that is both the anchor and the grant.</param>
    /// <returns>A policy anchored at and granting <paramref name="root"/>.</returns>
    private static PathPolicy CreateRootedPolicy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Extracts the single "Interpreted as:" line from a denial message, so an assertion can
    ///     examine the reported interpretation without matching text in the echoed request or the
    ///     enumerated locations.
    /// </summary>
    /// <param name="denial">The denial message to search.</param>
    /// <returns>The "Interpreted as:" line, or an empty string when the denial has none.</returns>
    private static string InterpretedLine(string denial)
    {
        return denial
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith("Interpreted as:", StringComparison.Ordinal))
            ?? string.Empty;
    }
}
