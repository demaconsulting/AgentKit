namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="PathPolicy"/> class.
/// </summary>
/// <remarks>
///     <para>
///     Request paths are built from the rule's resolved root where the scenario is about
///     containment, so that the tests describe what a caller holds after the root has been
///     resolved, and so that a host whose temporary directory is itself reached through a link
///     does not perturb the expectations.
///     </para>
///     <para>
///     <b>A second group of scenarios states paths the way a model states them</b> — a bare file
///     name, a leading current-directory token, a nested relative name, and no path at all.
///     Those spellings are how a request actually arrives, and a suite that only ever passes
///     fixture-absolute paths cannot observe how they are interpreted. That gap is what let a
///     defect in relative-path resolution survive a full test suite and four formal reviews.
///     </para>
/// </remarks>
public class PathPolicyTests
{
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
        var requested = Path.Combine(policy.ReadRule.Root!, "junction", "secret.txt");

        // Act: ask the policy to resolve the escaping path
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: the request is refused and no location is handed back
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that a path which passes a naive string prefix check is still refused once
    ///     its real location is known.
    /// </summary>
    /// <remarks>
    ///     This pins the reason containment cannot be a string comparison against the requested
    ///     path: the requested path genuinely starts with the permitted root, and is still an
    ///     escape.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_NaivePrefixCheckWouldPass_StillDenied()
    {
        // Arrange: a request whose text is contained by the root but whose target is not
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.ReadRule.Root!, "junction", "secret.txt");

        // Act: evaluate the naive check and the real policy decision
        var naiveCheckPasses = requested.StartsWith(
            policy.ReadRule.Root! + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
        var permitted = policy.TryResolveRead(requested, out _, out _);

        // Assert: the naive check is satisfied and the policy refuses anyway
        Assert.True(naiveCheckPasses);
        Assert.False(permitted);
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
        var requested = Path.Combine(policy.ReadRule.Root!, "notes.txt");

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
    /// <remarks>
    ///     An exception at a tool call ends the agent's turn; a returned denial lets the model
    ///     choose another path. This distinction is the reason the API is shaped as it is.
    /// </remarks>
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
    ///     Proves that a denial message discloses neither the requested path nor the permitted
    ///     location.
    /// </summary>
    /// <remarks>
    ///     The message is handed to a model and the resulting transcript leaves this process,
    ///     so it must not carry host layout with it.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_DeniedPath_DenialMessageOmitsRequestedPath()
    {
        // Arrange: a policy confined to the root, and a location outside it
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: resolve a path outside the permitted location
        policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: no part of the host's layout appears in the message
        Assert.NotNull(denialMessage);
        Assert.DoesNotContain(requested, denialMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(policy.ReadRule.Root!, denialMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            denialMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a path whose real location cannot be determined is refused rather than
    ///     allowed to throw.
    /// </summary>
    /// <remarks>
    ///     Denying the unknown is the fail-safe reading, and it keeps the non-throwing contract
    ///     intact for input the model controls.
    /// </remarks>
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

    /// <summary>
    ///     Proves that a read is judged by the read rule alone, even when the write rule would
    ///     permit the location.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_WriteRootOnly_DeniesReadOutsideReadRoot()
    {
        // Arrange: read confined to the root, write confined to the outside directory
        using var fixture = new ReparsePointFixture();
        var readRule = PathRule.Rooted(fixture.Root);
        var writeRule = PathRule.Rooted(fixture.Outside);
        var policy = new PathPolicy(readRule, writeRule);
        var requested = Path.Combine(writeRule.Root!, "elsewhere.txt");

        // Act: read a location only the write rule permits
        var permitted = policy.TryResolveRead(requested, out _, out _);

        // Assert: the write rule grants nothing to a read
        Assert.False(permitted);
        Assert.True(policy.TryResolveWrite(requested, out _, out _));
    }

    /// <summary>
    ///     Proves that a permitted write path is reported together with its real location.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveWrite_PermittedPath_ReturnsRealPath()
    {
        // Arrange: a policy confined to the root, and a file that does not exist yet
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(policy.WriteRule.Root!, "new-file.txt");

        // Act: resolve a contained write target
        var permitted = policy.TryResolveWrite(requested, out var realPath, out var denialMessage);

        // Assert: the caller receives the real location it may create
        Assert.True(permitted);
        Assert.Null(denialMessage);
        Assert.Equal(requested, realPath);
    }

    /// <summary>
    ///     Proves that a refused write produces a returned denial rather than an exception.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveWrite_DeniedPath_ReturnsFalseWithoutThrowing()
    {
        // Arrange: a policy confined to the root, and a location outside it
        using var fixture = new ReparsePointFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: resolve a write target outside the permitted location
        var permitted = policy.TryResolveWrite(requested, out var realPath, out var denialMessage);

        // Assert: refusal is reported by the return value, with a reason
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotEmpty(denialMessage!);
    }

    /// <summary>
    ///     Proves that a location the read rule permits is not thereby writable.
    /// </summary>
    /// <remarks>
    ///     This is the read-wide, write-narrow configuration that motivates keeping the two
    ///     rules independent.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveWrite_ReadableButNotWritablePath_ReturnsDenial()
    {
        // Arrange: unrestricted reads, writes confined to the root
        using var fixture = new ReparsePointFixture();
        var writeRule = PathRule.Rooted(fixture.Root);
        var policy = new PathPolicy(PathRule.Unrestricted(), writeRule);
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "readable.txt", "content");

        // Act: read and then attempt to write the same location
        var readPermitted = policy.TryResolveRead(requested, out _, out _);
        var writePermitted = policy.TryResolveWrite(requested, out _, out var denialMessage);

        // Assert: readable does not imply writable
        Assert.True(readPermitted);
        Assert.False(writePermitted);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that enumeration excludes a file that is only reachable by leaving the
    ///     permitted location.
    /// </summary>
    /// <remarks>
    ///     The test first asserts that a raw recursive enumeration does surface the escaped
    ///     file, so that it cannot silently become vacuous if the platform ever stops following
    ///     links during enumeration.
    /// </remarks>
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
            .EnumerateFiles(policy.ReadRule.Root!, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToArray();
        var policyFiles = policy
            .EnumerateFiles(policy.ReadRule.Root!, "*")
            .Select(Path.GetFileName)
            .ToArray();

        // Assert: raw enumeration crosses the link; the policy's enumeration does not
        Assert.Contains("secret.txt", rawFiles);
        Assert.DoesNotContain("secret.txt", policyFiles);
        Assert.Contains("inside.txt", policyFiles);
    }

    /// <summary>
    ///     Proves that enumeration lists the files the policy would permit a caller to read.
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
        var files = policy.EnumerateFiles(policy.ReadRule.Root!, "*").Select(Path.GetFileName).ToArray();

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
    ///     Proves that a policy cannot be created without a read rule.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NullReadRule_ThrowsArgumentNullException()
    {
        // Act & Assert: an unguarded policy must be unrepresentable
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(null!, PathRule.Unrestricted()));
    }

    /// <summary>
    ///     Proves that a policy cannot be created without a write rule.
    /// </summary>
    [Fact]
    public void PathPolicy_Constructor_NullWriteRule_ThrowsArgumentNullException()
    {
        // Act & Assert: both rules are required, not just the first
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(PathRule.Unrestricted(), null!));
    }

    /// <summary>
    ///     Proves that a policy created without explicit ceilings carries the library's
    ///     documented ones.
    /// </summary>
    /// <remarks>
    ///     The assertion is by reference rather than by value, so the two-rule constructor
    ///     cannot silently start allocating a fresh set of ceilings that merely happens to
    ///     agree with the default.
    /// </remarks>
    [Fact]
    public void PathPolicy_Constructor_NoLimits_UsesDefaultLimits()
    {
        // Arrange & Act: construct a policy without stating any ceilings
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

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
        var limits = new ToolLimits(maxBinaryBytes: 1024);

        // Act: construct a policy carrying those ceilings
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted(), limits);

        // Assert: the tools this policy governs observe the host's budget
        Assert.Same(limits, policy.Limits);
    }

    /// <summary>
    ///     Proves that a policy cannot be created with a missing set of ceilings.
    /// </summary>
    /// <remarks>
    ///     Unbounded is not a sensible default, so an absent set of ceilings is the same kind
    ///     of programming error as an absent rule.
    /// </remarks>
    [Fact]
    public void PathPolicy_Constructor_NullLimits_ThrowsArgumentNullException()
    {
        // Act & Assert: ceilings are required whenever they are stated explicitly
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted(), null!));
    }

    /// <summary>
    ///     Proves that naming a workspace sets the base directory and both rules from one
    ///     argument.
    /// </summary>
    /// <remarks>
    ///     The general constructor makes the broken configuration the easy one: two rooted rules
    ///     and no base says nothing about how a bare file name should be read. This shorthand
    ///     exists so that the correct configuration is the shorter one to write.
    /// </remarks>
    [Fact]
    public void PathPolicy_ForWorkspace_Root_SetsBaseAndBothRules()
    {
        // Arrange: a workspace directory
        using var fixture = new ReparsePointFixture();

        // Act: name the workspace once
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Assert: the base and both rules all denote the workspace
        var real = RealPathResolver.Resolve(fixture.Root);
        Assert.Equal(real, policy.BaseDirectory);
        Assert.Equal(real, policy.ReadRule.Root);
        Assert.Equal(real, policy.WriteRule.Root);
    }

    /// <summary>
    ///     Proves that a workspace policy cannot be created without a workspace.
    /// </summary>
    [Fact]
    public void PathPolicy_ForWorkspace_NullRoot_ThrowsArgumentException()
    {
        // Act &amp; Assert: there is no safe location to assume in place of a missing workspace
        Assert.ThrowsAny<ArgumentException>(() => PathPolicy.ForWorkspace(null!));
        Assert.ThrowsAny<ArgumentException>(() => PathPolicy.ForWorkspace(string.Empty));
    }

    /// <summary>
    ///     Proves that a workspace policy carries the ceilings the host supplied.
    /// </summary>
    [Fact]
    public void PathPolicy_ForWorkspace_CustomLimits_ExposesSuppliedLimits()
    {
        // Arrange: a workspace and a host that tightens the binary-content ceiling
        using var fixture = new ReparsePointFixture();
        var limits = new ToolLimits(maxBinaryBytes: 1024);

        // Act: name the workspace and the budget together
        var policy = PathPolicy.ForWorkspace(fixture.Root, limits);

        // Assert: the shorthand does not quietly substitute the defaults
        Assert.Same(limits, policy.Limits);
    }

    /// <summary>
    ///     Proves that a policy built without an explicit base interprets relative paths against
    ///     the read rule's location.
    /// </summary>
    /// <remarks>
    ///     A host that supplies two rooted rules and says nothing about a base has already
    ///     stated where its agent works. Defaulting to that location is what keeps a policy
    ///     built the ordinary way from behaving as though relative paths belonged to the process
    ///     working directory.
    /// </remarks>
    [Fact]
    public void PathPolicy_Constructor_NoBaseDirectory_UsesTheReadRuleRoot()
    {
        // Arrange: a rooted read rule and no stated base
        using var fixture = new ReparsePointFixture();

        // Act: construct the policy the ordinary way
        var policy = CreateRootedPolicy(fixture.Root);

        // Assert: the read rule's location is what a relative path is measured from
        Assert.Equal(policy.ReadRule.Root, policy.BaseDirectory);
    }

    /// <summary>
    ///     Proves that a workspace reached through a link is reported at its real location.
    /// </summary>
    /// <remarks>
    ///     The base is resolved once, at construction, for the same reason a rule's location is:
    ///     every later comparison is then real location against real location, so a workspace
    ///     that is itself a link still permits its own contents.
    /// </remarks>
    [Fact]
    public void PathPolicy_BaseDirectory_RootReachedThroughLink_IsReportedAsItsRealLocation()
    {
        // Arrange: a link inside the fixture pointing at a sibling directory
        using var fixture = new ReparsePointFixture();
        var link = fixture.CreateDirectoryLink("alias", fixture.Outside);

        // Act: name the link as the workspace
        var policy = PathPolicy.ForWorkspace(link);

        // Assert: the real location is what the policy holds, not the link's own path
        Assert.Equal(RealPathResolver.Resolve(fixture.Outside), policy.BaseDirectory);
    }

    /// <summary>
    ///     Proves that a bare file name — the path a model actually writes — resolves beneath the
    ///     workspace.
    /// </summary>
    /// <remarks>
    ///     This is the scenario the whole workspace base exists for. A model asks for
    ///     <c>notes.txt</c>; resolving that anywhere other than the workspace refuses a request
    ///     that was correct in every way the model could tell.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_BareFileName_ResolvesBeneathTheBase()
    {
        // Arrange: a workspace holding one file, addressed the way a model would address it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for the file by name alone
        var permitted = policy.TryResolveRead("notes.txt", out var realPath, out var denialMessage);

        // Assert: the real file, reached from the workspace
        Assert.True(permitted);
        Assert.Null(denialMessage);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a name prefixed with the current-directory token resolves beneath the
    ///     workspace.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_DotSlashFileName_ResolvesBeneathTheBase()
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for the file with the leading current-directory token a model often adds
        var permitted = policy.TryResolveRead("./notes.txt", out var realPath, out _);

        // Assert: the same file the bare name reaches
        Assert.True(permitted);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a nested relative path resolves beneath the workspace.
    /// </summary>
    /// <remarks>
    ///     The forward slash is deliberate: it is the separator a model writes regardless of the
    ///     host platform, so it must be the separator the policy accepts.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_NestedRelativePath_ResolvesBeneathTheBase()
    {
        // Arrange: a file one level below the workspace root
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "nested");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for it the way a model writes a nested path
        var permitted = policy.TryResolveRead("sub/child.txt", out var realPath, out _);

        // Assert: the nested file, reached from the workspace
        Assert.True(permitted);
        Assert.Equal("nested", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a relative path is not measured from the process working directory.
    /// </summary>
    /// <remarks>
    ///     This is the defect pinned as a regression. Resolving against the process working
    ///     directory looks like containment from the outside — every request is refused — while
    ///     being nothing of the kind.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativePath_IsNotResolvedAgainstTheProcessDirectory()
    {
        // Arrange: a workspace that is deliberately not the process working directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for the file by name alone
        policy.TryResolveRead("notes.txt", out var realPath, out _);

        // Assert: the workspace supplied the location, not wherever this process was started
        Assert.NotNull(realPath);
        Assert.StartsWith(policy.BaseDirectory, realPath, StringComparison.Ordinal);
        Assert.NotEqual(
            Path.Combine(Environment.CurrentDirectory, "notes.txt"),
            realPath);
    }

    /// <summary>
    ///     Proves that a relative path climbing out of the workspace is refused.
    /// </summary>
    /// <remarks>
    ///     Accepting relative paths must not weaken containment: the escape is judged on the
    ///     resolved location exactly as an absolute escape is.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativeParentTraversal_ReturnsDenial()
    {
        // Arrange: a secret in the sibling directory outside the workspace
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: climb out of the workspace with a relative path
        var permitted = policy.TryResolveRead(
            "../outside/secret.txt",
            out var realPath,
            out var denialMessage);

        // Assert: refused, with no location handed back
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that a relative path reaching outside the workspace through a link is refused.
    /// </summary>
    /// <remarks>
    ///     The per-component reparse-point walk runs on the path only after it has been made
    ///     absolute against the workspace, so a relative escape and an absolute one reach the
    ///     same decision.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_RelativePathBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a secret outside the workspace, reachable through a link inside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for the escaping path relatively, as a model would after a listing
        var permitted = policy.TryResolveRead(
            "junction/secret.txt",
            out var realPath,
            out var denialMessage);

        // Assert: refused, exactly as the absolute spelling of the same request is
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that an absolute path inside the workspace remains expressible.
    /// </summary>
    /// <remarks>
    ///     Naming a workspace narrows how a bare name is read; it does not withdraw the absolute
    ///     form, which a host composing paths itself still relies on.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_AbsolutePathInsideRoot_ReturnsRealPath()
    {
        // Arrange: a workspace holding one file, addressed absolutely
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = PathPolicy.ForWorkspace(fixture.Root);
        var requested = Path.Combine(policy.BaseDirectory, "notes.txt");

        // Act: ask for the file by its absolute location
        var permitted = policy.TryResolveRead(requested, out var realPath, out _);

        // Assert: permitted, and the same file the bare name reaches
        Assert.True(permitted);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that an absolute path outside the workspace is still refused.
    /// </summary>
    [Fact]
    public void PathPolicy_TryResolveRead_AbsolutePathOutsideRoot_ReturnsDenial()
    {
        // Arrange: a file in the sibling directory outside the workspace
        using var fixture = new ReparsePointFixture();
        var requested = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask for it by its absolute location
        var permitted = policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: containment is unchanged by the workspace base
        Assert.False(permitted);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that an omitted path denotes the workspace itself rather than raising an error.
    /// </summary>
    /// <param name="path">The spelling of "no path" the caller supplied.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PathPolicy_TryResolveRead_OmittedPath_ResolvesToTheBase(string? path)
    {
        // Arrange: a workspace a model has not yet named
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: resolve the omitted request
        var permitted = policy.TryResolveRead(path, out var realPath, out _);

        // Assert: the workspace itself, with no exception escaping
        Assert.True(permitted);
        Assert.Equal(policy.BaseDirectory, realPath);
    }

    /// <summary>
    ///     Proves that the literal words a model sends for "no path" denote the workspace.
    /// </summary>
    /// <remarks>
    ///     A model whose schema marks an argument optional frequently sends the word its own
    ///     runtime prints for absence rather than omitting the argument. Reading that as a file
    ///     name refuses a well-formed request.
    /// </remarks>
    /// <param name="path">The placeholder spelling the caller supplied.</param>
    [Theory]
    [InlineData("None")]
    [InlineData("null")]
    [InlineData("NONE")]
    public void PathPolicy_TryResolveRead_PlaceholderPath_ResolvesToTheBase(string path)
    {
        // Arrange: a workspace a model has not yet named
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: resolve the placeholder request
        var permitted = policy.TryResolveRead(path, out var realPath, out _);

        // Assert: the workspace itself, exactly as an omitted argument produces
        Assert.True(permitted);
        Assert.Equal(policy.BaseDirectory, realPath);
    }

    /// <summary>
    ///     Proves that a bare file name resolves beneath the workspace for writing too.
    /// </summary>
    /// <remarks>
    ///     Reads and writes must interpret a relative path identically, or an agent could read a
    ///     file it then cannot write back under the same name.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveWrite_BareFileName_ResolvesBeneathTheBase()
    {
        // Arrange: a workspace and a file that does not exist yet
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: ask to write the file by name alone
        var permitted = policy.TryResolveWrite("new-file.txt", out var realPath, out _);

        // Assert: the location beneath the workspace the caller may create
        Assert.True(permitted);
        Assert.Equal(Path.Combine(policy.BaseDirectory, "new-file.txt"), realPath);
    }

    /// <summary>
    ///     Proves that an omitted write path is answered rather than thrown at the caller.
    /// </summary>
    /// <remarks>
    ///     The workspace itself is a directory, so the request is answered and then refused by a
    ///     tool for being a directory; what matters here is that no caller-supplied path — not
    ///     even an absent one — leaves this method as an exception.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveWrite_OmittedPath_DoesNotThrow()
    {
        // Arrange: a workspace
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: resolve a write with no path at all
        var permitted = policy.TryResolveWrite(null, out var realPath, out _);

        // Assert: a returned answer, not an exception
        Assert.True(permitted);
        Assert.Equal(policy.BaseDirectory, realPath);
    }

    /// <summary>
    ///     Proves that an omitted directory enumerates the workspace.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_OmittedDirectory_ListsTheBase()
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: enumerate with no directory supplied
        var files = policy.EnumerateFiles(null, "*").Select(Path.GetFileName).ToArray();

        // Assert: the workspace is what "no directory" means
        Assert.Contains("top.txt", files);
    }

    /// <summary>
    ///     Proves that a relative directory enumerates that directory beneath the workspace.
    /// </summary>
    [Fact]
    public void PathPolicy_EnumerateFiles_RelativeDirectory_ListsThatDirectory()
    {
        // Arrange: one file in a subdirectory and one outside it but still in the workspace
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "deep.txt", "deep");
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var policy = PathPolicy.ForWorkspace(fixture.Root);

        // Act: enumerate the subdirectory by its relative name
        var files = policy.EnumerateFiles("sub", "*").Select(Path.GetFileName).ToArray();

        // Assert: the named subdirectory, not the whole workspace
        Assert.Equal(["deep.txt"], files);
    }

    /// <summary>
    ///     Proves that a denial tells the model what form a request should take.
    /// </summary>
    /// <remarks>
    ///     A denial that only refuses leaves a model re-submitting variations of the same path
    ///     until it gives up. Naming the expected form is what turns a refusal into a step the
    ///     agent can recover from.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_DeniedPath_DenialMessageStatesTheExpectedPathForm()
    {
        // Arrange: a workspace, and a request outside it
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request the refused location
        policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: the message says what to do instead, not merely that the answer is no
        Assert.NotNull(denialMessage);
        Assert.Contains("workspace root", denialMessage, StringComparison.Ordinal);
        Assert.Contains("notes.txt", denialMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the recovery guidance introduces no directory separator.
    /// </summary>
    /// <remarks>
    ///     "Contains no separator" is the usable test for "contains no host location", so the
    ///     guidance must be phrased with an example that carries none. A nested example such as
    ///     a path with a slash in it would silently retire that check.
    /// </remarks>
    [Fact]
    public void PathPolicy_TryResolveRead_DeniedPath_DenialMessageContainsNoDirectorySeparator()
    {
        // Arrange: a workspace, and a request outside it
        using var fixture = new ReparsePointFixture();
        var policy = PathPolicy.ForWorkspace(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request the refused location
        policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: neither separator appears, in any position
        Assert.NotNull(denialMessage);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            denialMessage,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.AltDirectorySeparatorChar.ToString(),
            denialMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy whose read and write access are both confined to one location.
    /// </summary>
    /// <remarks>
    ///     The common shape for containment tests, kept in one place so that each test states
    ///     only what makes it different.
    /// </remarks>
    /// <param name="root">The location to confine both rules to.</param>
    /// <returns>A policy rooted at <paramref name="root"/> for both reads and writes.</returns>
    private static PathPolicy CreateRootedPolicy(string root)
    {
        return new PathPolicy(PathRule.Rooted(root), PathRule.Rooted(root));
    }
}
