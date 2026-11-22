using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Unit tests for the <see cref="RenderDestination"/> class.
/// </summary>
/// <remarks>
///     Each refusal is asserted with its reason and its fixed text, and each write is asserted
///     against the bytes on disk, because a destination check that looked right but wrote
///     somewhere else — or left a temporary file behind — is exactly what these scenarios exist to
///     catch.
/// </remarks>
public class RenderDestinationTests
{
    /// <summary>
    ///     A byte sequence standing in for an encoded picture.
    /// </summary>
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    /// <summary>
    ///     Proves a destination not named as a PNG file is refused before any policy decision.
    /// </summary>
    [Fact]
    public void RenderDestination_TryResolve_NotPng_IsRefusedAsInvalidRequest()
    {
        // Arrange: a writable workspace
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: name a JPEG
        var resolved = RenderDestination.TryResolve(policy, "figure.jpg", out _, out var denial);

        // Assert: refused, stating why
        Assert.False(resolved);
        Assert.Contains("Denied (InvalidRequest)", Assert.IsType<string>(denial), StringComparison.Ordinal);
        Assert.Contains(RenderDestination.MustBePng, (string)denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination the policy's write decision refuses is refused with the policy's
    ///     own text.
    /// </summary>
    [Fact]
    public void RenderDestination_TryResolve_ReadOnlyLocation_IsRefusedByTheWriteDecision()
    {
        // Arrange: a read-only workspace
        using var fixture = new TempDirectoryFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);

        // Act: name a destination in it
        var resolved = RenderDestination.TryResolve(policy, "figure.png", out _, out var denial);

        // Assert: the write decision refused it, naming the location's access
        Assert.False(resolved);
        Assert.StartsWith("Denied (PathNotPermitted)", Assert.IsType<string>(denial), StringComparison.Ordinal);
        Assert.Contains("read-only", (string)denial, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination naming a directory is refused.
    /// </summary>
    [Fact]
    public void RenderDestination_TryResolve_Directory_IsRefused()
    {
        // Arrange: a directory whose name ends in .png
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "pictures.png"));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: name it
        var resolved = RenderDestination.TryResolve(policy, "pictures.png", out _, out var denial);

        // Assert: refused as a directory
        Assert.False(resolved);
        Assert.Contains(RenderDestination.IsDirectory, Assert.IsType<string>(denial), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination whose parent directory does not exist is refused, and no directory
    ///     is created.
    /// </summary>
    [Fact]
    public void RenderDestination_TryResolve_MissingParent_IsRefusedWithoutCreatingIt()
    {
        // Arrange: a writable workspace
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: name a destination in a folder that does not exist
        var resolved = RenderDestination.TryResolve(policy, "missing/figure.png", out _, out var denial);

        // Assert: refused as a missing target, and nothing was created
        Assert.False(resolved);
        Assert.StartsWith("Denied (TargetNotFound)", Assert.IsType<string>(denial), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "missing")));
    }

    /// <summary>
    ///     Proves a picture is written to a permitted destination, reported relative to the
    ///     workspace, and replaces an earlier one of the same name.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task RenderDestination_TryWriteAsync_ExistingFile_IsReplaced()
    {
        // Arrange: an earlier rendering at the destination
        using var fixture = new TempDirectoryFixture();
        var target = TempDirectoryFixture.WriteBytes(fixture.Root, "figure.png", [1, 2, 3]);
        var policy = RenderSamples.Policy(fixture.Root);
        Assert.True(RenderDestination.TryResolve(policy, "figure.png", out var destination, out _));

        // Act: write the new rendering
        var written = await RenderDestination.TryWriteAsync(Picture, destination, TestContext.Current.CancellationToken);

        // Assert: replaced, reported relatively, and no temporary file left behind
        Assert.True(written);
        Assert.Equal("figure.png", destination.Reported);
        Assert.Equal(Picture, await System.IO.File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    /// <summary>
    ///     Proves a write the file system refuses reports failure and leaves no temporary file.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task RenderDestination_TryWriteAsync_FileSystemRefuses_LeavesNoTemporaryFile()
    {
        // Arrange: a destination resolved, then its target replaced by a non-empty directory
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        Assert.True(RenderDestination.TryResolve(policy, "figure.png", out var destination, out _));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "figure.png", "inner"));

        // Act: try to write
        var written = await RenderDestination.TryWriteAsync(Picture, destination, TestContext.Current.CancellationToken);

        // Assert: refused, and the folder holds no leftover
        Assert.False(written);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }
}
