using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.CanvasNet.Viewer.Help;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     The diagram tool family: the pack an application attaches to let an agent draw diagrams —
///     Mermaid, PlantUML, SVG, Vega-Lite and OpenSCAD — check what it drew, fix it and save the
///     picture, with the drawing help for each language a topic at a time.
/// </summary>
/// <remarks>
///     <para>
///     The family publishes two tools: <c>diagram_help</c>, which serves the drawing help of the
///     offered languages, and <c>diagram_render</c>, which draws source the model supplies inline.
///     Together they make one loop — read the card for the diagram type, draw, look at the result,
///     read the diagnostics topic a failure names, fix and draw again — so they are created
///     together, and a pack is the only public way to obtain them.
///     </para>
///     <para>
///     <b>The preview mode follows the host.</b> In <see cref="DiagramPreview.Image"/> mode the
///     picture is returned to the model to look at, so the pack requires
///     <see cref="HostCapabilities.Vision"/>. In <see cref="DiagramPreview.Text"/> mode the picture
///     is saved and the model receives a description of it in words, so the pack requires nothing
///     and a model that cannot see can still draw. <see cref="For"/> chooses the mode from the host's
///     capabilities. The mode is a constructor argument rather than an option, so that
///     <see cref="For"/> never has to override a value its caller set.
///     </para>
///     <para>
///     <b>Publication follows the policy only where it must.</b> <c>diagram_help</c> touches no file
///     and is published under every policy. In image preview, <c>diagram_render</c> is published
///     under every policy too, because returning the picture writes nothing; its optional
///     <c>save_as</c> needs a write grant, and naming one under a read-only policy earns the ordinary
///     refusal naming the writable locations. In text preview, <c>save_as</c> is required, so under a
///     policy that permits no writing anywhere the tool's only outcome would be a refusal, and it is
///     withheld.
///     </para>
///     <para>
///     <b>Instructions are offered, never injected.</b> <see cref="Instructions"/> is a short
///     paragraph naming the offered languages and the draw, look, fix loop, for an application to
///     place in its agent's instructions if it wants to. The pack never adds it anywhere itself:
///     an application's instructions are its own.
///     </para>
///     <para>
///     <b>The pack's state is its options, its filtered help catalog and its render gate.</b> The
///     contract expects a pack to be stateless; this one holds immutable options and catalog, and a
///     gate bounding how many drawings run at once
///     (<see cref="VisualOptions.MaxConcurrentRenders"/>). The gate is shared, by design, by every
///     composition the instance is added to, so the bound holds per pack instance however many
///     agents it serves. All three are safe for concurrent use, so the class is safe for
///     concurrent use from any number of threads.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so
///     that a test or a composing application can name the family without repeating a string
///     literal that could drift from the names the tools actually carry.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     The preview mode chosen from the host's capabilities, and the pack's instructions placed in
///     the agent's own instructions by the application.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(workspace, [PathRule.ReadWrite(workspace)]);
///
///     var host = HostCapabilities.Vision;
///     var diagrams = DiagramPack.For(host);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .WithHostCapabilities(host)
///         .Add(diagrams)
///         .Build();
///
///     var instructions = "You write documentation. " + diagrams.Instructions;
///     </code>
/// </example>
public sealed class DiagramPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "diagram";

    /// <summary>
    ///     The renderer every <c>diagram_render</c> tool this pack creates draws through.
    /// </summary>
    private readonly VisualRenderer _renderer;

    /// <summary>
    ///     The help catalog filtered to the offered languages, shared by the pack's tools.
    /// </summary>
    private readonly HelpCatalog _catalog;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DiagramPack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack grants nothing on its own: the policy that governs its tools comes from the
    ///     <see cref="ToolPackBuilder"/> the pack is added to, and the vision capability image
    ///     preview requires is declared on that builder. Calling the constructor with no argument is
    ///     the same as passing <see cref="DiagramPreview.Image"/> and
    ///     <see cref="DiagramPackOptions.Default"/>.
    /// </remarks>
    /// <param name="preview">How <c>diagram_render</c> shows the model what it drew.</param>
    /// <param name="options">
    ///     The rendering bounds and the offered languages, or <see langword="null"/> for the
    ///     defaults. <see cref="VisualOptions.MinPreviewSide"/> may not exceed
    ///     <see cref="VisualOptions.MaxRenderSide"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="preview"/> is not a defined <see cref="DiagramPreview"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <see cref="VisualOptions.MinPreviewSide"/> exceeds
    ///     <see cref="VisualOptions.MaxRenderSide"/>.
    /// </exception>
    public DiagramPack(DiagramPreview preview = DiagramPreview.Image, DiagramPackOptions? options = null)
    {
        if (!Enum.IsDefined(preview))
        {
            throw new ArgumentOutOfRangeException(nameof(preview), preview, "The preview mode is not defined.");
        }

        Preview = preview;
        Options = options ?? DiagramPackOptions.Default;

        // The one relation the options cannot check themselves, because an initializer sets the
        // two values one at a time.
        if (Options.Visual.MinPreviewSide > Options.Visual.MaxRenderSide)
        {
            throw new ArgumentException(
                "The smallest preview side may not exceed the largest render side.",
                nameof(options));
        }

        _renderer = new VisualRenderer(Options.Visual);
        _catalog = DiagramHelpTool.CatalogFor(Options.Formats);
        Instructions = ComposeInstructions(preview, Options.Formats);
    }

    /// <summary>
    ///     Gets how <c>diagram_render</c> shows the model what it drew.
    /// </summary>
    public DiagramPreview Preview { get; }

    /// <summary>
    ///     Gets the rendering bounds and the offered languages.
    /// </summary>
    public DiagramPackOptions Options { get; }

    /// <summary>
    ///     Gets a short paragraph an application may add to its agent's instructions, naming the
    ///     offered languages and the draw, look, fix loop.
    /// </summary>
    /// <remarks>
    ///     About eighty tokens. The text follows the preview mode: in text preview it says that
    ///     <c>save_as</c> is required and a description is returned instead of the picture. The pack
    ///     never adds it to any instructions itself.
    /// </remarks>
    public string Instructions { get; }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so that the constant above can keep the name the contract
    ///     uses. The class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.Vision"/> in image preview, because the picture is returned
    ///     to the model to look at; <see cref="HostCapabilities.None"/> in text preview, because the
    ///     model is given a description in words.
    /// </remarks>
    public HostCapabilities RequiredCapabilities =>
        Preview == DiagramPreview.Image ? HostCapabilities.Vision : HostCapabilities.None;

    /// <summary>
    ///     Creates a pack whose preview mode suits a host: image preview when the host declares
    ///     <see cref="HostCapabilities.Vision"/>, text preview otherwise.
    /// </summary>
    /// <remarks>
    ///     Pass the same capabilities the application declares on its <see cref="ToolPackBuilder"/>,
    ///     so the pack is never withheld for a capability it does not need.
    /// </remarks>
    /// <param name="host">The capabilities the host declares.</param>
    /// <param name="options">
    ///     The rendering bounds and the offered languages, or <see langword="null"/> for the defaults.
    /// </param>
    /// <returns>The pack.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <see cref="VisualOptions.MinPreviewSide"/> exceeds
    ///     <see cref="VisualOptions.MaxRenderSide"/>.
    /// </exception>
    public static DiagramPack For(HostCapabilities host, DiagramPackOptions? options = null)
    {
        return new DiagramPack(
            host.HasFlag(HostCapabilities.Vision) ? DiagramPreview.Image : DiagramPreview.Text,
            options);
    }

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <c>diagram_help</c> is created under every policy. <c>diagram_render</c> is created under
    ///     every policy in image preview, and in text preview only when the policy permits writing
    ///     somewhere, because text preview requires <c>save_as</c> and its only outcome under a
    ///     read-only policy would be a refusal. The policy is captured by the tool's factory, so no
    ///     tool can later observe a different policy; the render tool also receives the pack's
    ///     renderer, so every drawing of the pack shares one render gate.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>The help tool, and the render tool unless the policy withholds it.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to the tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        var help = DiagramHelpTool.Create(_catalog, Options.Formats);
        if (Preview == DiagramPreview.Text && !policy.AnyLocationIsWritable)
        {
            return [help];
        }

        return [help, DiagramRenderTool.Create(policy, _renderer, Preview, Options.Formats, _catalog)];
    }

    /// <summary>
    ///     Draws a small sample of every offered language, so that the rendering library's
    ///     first-use cost is paid before a model is waiting.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Optional, and never called by the pack itself. The first drawing of each language in a
    ///     process pays for loading and initializing its library, which can take a second or more;
    ///     a host that would rather pay that at start-up than during a conversation calls this
    ///     once, off its hot path.
    ///     </para>
    ///     <para>
    ///     The samples go through the pack's render gate and timeout like any drawing, reference no
    ///     file, and are discarded. A sample that fails is ignored: the first real drawing that
    ///     meets the same failure reports it.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">A token that stops the warm-up.</param>
    /// <returns>A task that completes when every sample was drawn.</returns>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        return _renderer.WarmUpAsync(Options.Formats, cancellationToken);
    }

    /// <summary>
    ///     Composes the paragraph <see cref="Instructions"/> returns.
    /// </summary>
    /// <param name="preview">The preview mode.</param>
    /// <param name="formats">The offered languages.</param>
    /// <returns>The paragraph.</returns>
    private static string ComposeInstructions(DiagramPreview preview, IReadOnlyList<string> formats)
    {
        var languages = string.Join(", ", formats);
        return preview == DiagramPreview.Image
            ? "You can draw diagrams (" + languages + "). Before drawing, read the diagram_help topic for "
              + "the diagram type. Call diagram_render with the complete source, look at the picture it "
              + "returns, and fix the source until the picture shows what you meant. A failed render "
              + "names the line, the column and a help topic to read. Use save_as to keep the picture."
            : "You can draw diagrams (" + languages + "). Before drawing, read the diagram_help topic for "
              + "the diagram type. Call diagram_render with the complete source and a save_as .png path; "
              + "you receive a description of the picture instead of the picture, so check its labels "
              + "and size, and fix the source until it is right. A failed render names the line, the "
              + "column and a help topic to read.";
    }
}
