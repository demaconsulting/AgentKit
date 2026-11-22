using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Image;
using DemaConsulting.CanvasNet.Viewer;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Unit tests for the <see cref="VisualAccess"/> class.
/// </summary>
/// <remarks>
///     <para>
///     The mapping is verified behaviorally: each scenario draws a real document whose reference
///     points somewhere specific, through the real rendering library, under the policy the mapping
///     produced, and asserts whether the referenced file was used. A policy that merely looked
///     right would pass an inspection of its parts and fail here.
///     </para>
///     <para>
///     Every escape route the rendering spike exercised is a scenario of its own: a parent-folder
///     escape, an absolute path, a junction or symbolic link, a <c>file:</c> URI, and a deny
///     pattern inside a grant — for embedded pictures, and for the included and data files of the
///     other formats.
///     </para>
/// </remarks>
public class VisualAccessTests
{
    /// <summary>
    ///     Proves a picture inside the grant is drawn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_PictureInsideTheGrant_IsDrawn()
    {
        // Arrange: a picture inside the grant, referenced relatively
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw an SVG embedding it
        var outcome = await RenderAsync(RenderSamples.SvgWithImage("ok.png"), "figure.svg", policy, fixture.Root);

        // Assert: drawn, with no note about the picture
        Assert.True(outcome.Succeeded);
        Assert.DoesNotContain(outcome.Notes, note => note.Contains("ok.png", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a picture reached by a parent-folder escape is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_ParentFolderEscape_IsRefused()
    {
        // Arrange: a picture in the sibling folder outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw an SVG escaping to it
        var outcome = await RenderAsync(RenderSamples.SvgWithImage("../outside/secret.png"), "figure.svg", policy, fixture.Root);

        // Assert: the refused picture fails the render rather than leaving a hole
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a picture named by an absolute path outside the grant is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_AbsolutePathOutside_IsRefused()
    {
        // Arrange: a picture outside the grant, named absolutely
        using var fixture = new TempDirectoryFixture();
        var secret = TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw an SVG embedding it
        var outcome = await RenderAsync(RenderSamples.SvgWithImage(secret), "figure.svg", policy, fixture.Root);

        // Assert: refused
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a picture reached through a directory link inside the grant that points outside
    ///     it is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_LinkEscape_IsRefused()
    {
        // Arrange: a junction (Windows) or symbolic link (elsewhere) inside the grant, pointing out
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(40, 20));
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "jn"), fixture.Outside);
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw an SVG reaching the picture through the link
        var outcome = await RenderAsync(RenderSamples.SvgWithImage("jn/secret.png"), "figure.svg", policy, fixture.Root);

        // Assert: refused, although the path as written lies inside the grant
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a picture inside the grant that a deny pattern excludes is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_DenyPatternInsideTheGrant_IsRefused()
    {
        // Arrange: a grant that excludes one name pattern, and a picture matching it
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "hidden.secret.png", ImageTestImages.Png(40, 20));
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root, ["*.secret.png"])]);

        // Act: draw an SVG embedding it
        var outcome = await RenderAsync(RenderSamples.SvgWithImage("hidden.secret.png"), "figure.svg", policy, fixture.Root);

        // Assert: refused by AgentKit's own decision, not merely by the grant's root
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a picture named by a <c>file:</c> URI outside the grant is refused, and one inside
    ///     it is drawn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_FileUri_IsJudgedLikeAPath()
    {
        // Arrange: one picture on each side of the grant
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(40, 20));
        var outside = TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw an SVG embedding each by URI
        var refused = await RenderAsync(RenderSamples.SvgWithImage(new Uri(outside).AbsoluteUri), "figure.svg", policy, fixture.Root);
        var drawn = await RenderAsync(RenderSamples.SvgWithImage(new Uri(inside).AbsoluteUri), "figure.svg", policy, fixture.Root);

        // Assert: the URI form is no way around the grant
        Assert.Equal(ErrorCodes.FileAccessDenied, refused.Failure?.Code);
        Assert.True(drawn.Succeeded);
    }

    /// <summary>
    ///     Proves an included or data file outside the grant is refused for every format that
    ///     reads one.
    /// </summary>
    /// <param name="format">The format under test.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("plantuml")]
    [InlineData("openscad")]
    [InlineData("vega-lite")]
    public async Task VisualAccess_ToFileAccess_IncludedFileOutside_IsRefused(string format)
    {
        // Arrange: an include or data file outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Outside, "inc.puml", "Bob -> Alice : secret\n");
        TempDirectoryFixture.WriteFile(fixture.Outside, "inc.scad", "sphere(2);\n");
        TempDirectoryFixture.WriteFile(fixture.Outside, "data.csv", "a,b\nx,1\n");
        var policy = RenderSamples.Policy(fixture.Root);
        var (source, fileName) = format switch
        {
            "plantuml" => (RenderSamples.PlantUmlInclude("../outside/inc.puml"), "diagram.puml"),
            "openscad" => (RenderSamples.ScadInclude("../outside/inc.scad"), "model.scad"),
            _ => (RenderSamples.VegaLiteDataUrl("../outside/data.csv"), "chart.vl.json")
        };

        // Act: draw the source
        var outcome = await RenderAsync(source, fileName, policy, fixture.Root);

        // Assert: refused as a file-access failure, whatever the format
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves an unrestricted grant omits the root confinement and still applies AgentKit's own
    ///     decision.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_UnrestrictedGrant_UsesThePolicyDecision()
    {
        // Arrange: an unrestricted grant that excludes one name pattern
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(40, 20));
        TempDirectoryFixture.WriteBytes(fixture.Root, "hidden.secret.png", ImageTestImages.Png(40, 20));
        var policy = new PathPolicy(fixture.Root, [PathRule.Unrestricted(AccessLevel.ReadOnly, ["*.secret.png"])]);

        // Act: map the policy, and draw one permitted and one excluded picture
        var access = VisualAccess.ToFileAccess(policy);
        var drawn = await RenderAsync(RenderSamples.SvgWithImage("ok.png"), "figure.svg", policy, fixture.Root);
        var refused = await RenderAsync(RenderSamples.SvgWithImage("hidden.secret.png"), "figure.svg", policy, fixture.Root);

        // Assert: no root confinement, but the delegate still decides
        Assert.DoesNotContain("roots", access.Name, StringComparison.Ordinal);
        Assert.Contains(VisualAccess.DelegateName, access.Name, StringComparison.Ordinal);
        Assert.Contains("no-symlinks", access.Name, StringComparison.Ordinal);
        Assert.True(drawn.Succeeded);
        Assert.Equal(ErrorCodes.FileAccessDenied, refused.Failure?.Code);
    }

    /// <summary>
    ///     Proves a rooted policy is mapped to all three parts and never to the library's
    ///     allow-everything policy.
    /// </summary>
    [Fact]
    public void VisualAccess_ToFileAccess_RootedPolicy_CombinesAllThreeParts()
    {
        // Arrange: a rooted grant
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: map it
        var access = VisualAccess.ToFileAccess(policy);

        // Assert: roots, no links, and AgentKit's decision, in that order
        Assert.Equal("roots+no-symlinks+" + VisualAccess.DelegateName, access.Name);
    }

    /// <summary>
    ///     Proves a refusal the mapping produces discloses no host absolute path once it is shaped
    ///     for a model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_Refusal_DisclosesNoHostPath()
    {
        // Arrange: a picture the delegate refuses
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "hidden.secret.png", ImageTestImages.Png(40, 20));
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root, ["*.secret.png"])]);
        var outcome = await RenderAsync(RenderSamples.SvgWithImage("hidden.secret.png"), "figure.svg", policy, fixture.Root);

        // Act: shape the refusal the way a tool does
        var text = RenderErrorShaper.Shape(outcome.Failure!, new ShapeContext(policy, fixture.Root, null, "figure.svg", null));

        // Assert: the fixed reason, and no host location
        Assert.Contains(VisualAccess.OutsideReason, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file-access policy '", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a network reference is never fetched.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualAccess_ToFileAccess_NetworkReference_IsNeverFetched()
    {
        // Arrange: a local listener counting every request, and an SVG referencing it
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        using var listener = CountingListener.Start();

        // Act: draw the SVG
        var outcome = await RenderAsync(RenderSamples.SvgWithImage(listener.Url + "x.png"), "figure.svg", policy, fixture.Root);

        // Assert: drawn without the picture, and the listener never saw a request
        Assert.True(outcome.Succeeded);
        Assert.Equal(0, listener.Hits);
    }

    /// <summary>
    ///     Proves inline source resolves references against a readable working directory, and
    ///     against no folder when the working directory is not readable.
    /// </summary>
    [Fact]
    public void VisualAccess_InlineBaseFolder_WorkingDirectoryReadability_DecidesTheFolder()
    {
        // Arrange: one policy granting its working directory, one granting only a subfolder
        using var fixture = new TempDirectoryFixture();
        var sub = Directory.CreateDirectory(Path.Combine(fixture.Root, "sub")).FullName;
        var granted = RenderSamples.Policy(fixture.Root);
        var ungranted = new PathPolicy(fixture.Root, [PathRule.ReadWrite(sub)]);

        // Act: choose the folder under each
        var grantedFolder = VisualAccess.InlineBaseFolder(granted);
        var ungrantedFolder = VisualAccess.InlineBaseFolder(ungranted);

        // Assert: the working directory only when it may be read; otherwise fail closed
        Assert.Equal(Path.GetFullPath(fixture.Root), grantedFolder);
        Assert.Null(ungrantedFolder);
    }

    /// <summary>
    ///     Draws a source through a fresh renderer.
    /// </summary>
    /// <param name="source">The source text.</param>
    /// <param name="fileName">The file name it is drawn as.</param>
    /// <param name="policy">The access policy to map.</param>
    /// <param name="folder">The folder references resolve against.</param>
    /// <returns>The outcome.</returns>
    private static Task<RenderOutcome> RenderAsync(string source, string fileName, PathPolicy policy, string folder)
    {
        return new VisualRenderer(VisualOptions.Default)
            .RenderAsync(RenderSamples.Request(source, fileName, policy, folder), TestContext.Current.CancellationToken);
    }
}
