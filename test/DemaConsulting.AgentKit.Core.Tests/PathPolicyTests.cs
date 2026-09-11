namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="PathPolicy"/> class.
/// </summary>
/// <remarks>
///     Request paths are built from the rule's resolved root so that the tests describe what a
///     caller would actually pass after the root has been resolved, and so that a host whose
///     temporary directory is itself reached through a link does not perturb the expectations.
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
