using System.ComponentModel;
using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Viewer.Help;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     The <c>diagram_help</c> tool: returns the drawing help for the diagram languages the pack
///     offers, one topic at a time — the index of topics, or one topic's card with its syntax, its
///     example sources and the topics below it.
/// </summary>
/// <remarks>
///     <para>
///     <b>Progressive, because the whole of it would crowd out the work.</b> The rendering
///     library's help is about seventy thousand tokens for every card at once. With no topic the
///     tool returns only the top-level topics, a few hundred tokens; a topic returns one card,
///     typically under two thousand, which names the topics below and beside it. A model reads the
///     one card for the diagram it is drawing, rather than carrying every language's reference in
///     its context.
///     </para>
///     <para>
///     <b>Only help the model can use is served.</b> The catalog is filtered once per pack: the
///     cards of a language the pack does not offer, the cards of formats the family never draws from
///     source (PDF, slides, drawings, raster images), and the cards written for the application
///     rather than the model — hosting options, file-access configuration and resource limits — are
///     hidden. A hidden card cannot be found by its id or an alias, and it does not appear in a
///     card's list of topics. A card about two languages is hidden when either is not offered.
///     </para>
///     <para>
///     The tool takes no path and touches no file, so it is published under every access policy.
///     The class is static; a constructed tool captures only the pack's immutable filtered catalog,
///     so it is safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class DiagramHelpTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant so that a shaped render failure naming it cannot drift from the
    ///     name actually registered.
    /// </remarks>
    public const string ToolName = "diagram_help";

    /// <summary>
    ///     The most topic ids a refusal of an unknown topic suggests.
    /// </summary>
    internal const int MaxSuggestions = 8;

    /// <summary>
    ///     The longest part of an unknown topic a refusal repeats, in characters.
    /// </summary>
    private const int MaxEchoedTopic = 80;

    /// <summary>
    ///     The help tags of cards written for the application rather than the model, always hidden.
    /// </summary>
    private static readonly string[] HostTags = ["hosting", "file-access", "limits"];

    /// <summary>
    ///     Builds the help catalog a pack serves: the rendering library's catalog with every card a
    ///     model drawing in the offered languages cannot use hidden.
    /// </summary>
    /// <param name="formats">The canonical names of the offered languages. Must not be null.</param>
    /// <returns>The filtered catalog.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="formats"/> is null.</exception>
    internal static HelpCatalog CatalogFor(IReadOnlyList<string> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        // Every card is included unless one of its tags is excluded: a language not offered, a
        // format never drawn from source, or a card written for the application.
        var offered = formats.Select(DiagramFormats.HelpTag).ToHashSet(StringComparer.Ordinal);
        var exclude = DiagramFormats.All
            .Select(DiagramFormats.HelpTag)
            .Where(tag => !offered.Contains(tag))
            .Concat(DiagramFormats.DocumentTags)
            .Concat(HostTags)
            .ToArray();

        return HelpCatalog.Default.Filter([], exclude);
    }

    /// <summary>
    ///     Creates the <c>diagram_help</c> tool over a pack's filtered catalog.
    /// </summary>
    /// <remarks>
    ///     Internal because a pack is the unit of attachment: an application obtains this tool by
    ///     attaching <see cref="DiagramPack"/>, which filters the catalog to the languages it offers.
    /// </remarks>
    /// <param name="catalog">The pack's filtered catalog. Must not be null.</param>
    /// <param name="formats">The canonical names of the offered languages. Must not be null.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static AIFunction Create(HelpCatalog catalog, IReadOnlyList<string> formats)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(formats);

        var description =
            "Returns drawing help for the diagram languages diagram_render draws ("
            + string.Join(", ", formats)
            + "). With no topic, returns the index of topics; with a topic id such as '"
            + formats[0]
            + "', returns that topic's card: what can be drawn, the syntax, example sources and the "
            + "topics below and beside it. Read the card of the diagram type you are drawing before "
            + "drawing it, and the diagnostics topic a failed render names.";

        var help = (
            [Description(
                "The topic id to read, for example 'mermaid' or 'mermaid/flowchart'. Omit it for the "
                + "index of topics.")]
            string? topic = null) => Help(catalog, formats, topic);

        return GuardedToolFactory.Create(help, ToolName, description);
    }

    /// <summary>
    ///     Answers one help request.
    /// </summary>
    /// <param name="catalog">The pack's filtered catalog.</param>
    /// <param name="formats">The offered languages.</param>
    /// <param name="topic">The topic the model asked for, or null for the index.</param>
    /// <returns>The index, the topic's card, or a refusal suggesting topics.</returns>
    internal static object Help(HelpCatalog catalog, IReadOnlyList<string> formats, string? topic)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(formats);

        if (string.IsNullOrWhiteSpace(topic))
        {
            return ToolResult.Text(Index(catalog, formats));
        }

        var wanted = topic.Trim();
        var card = catalog.Find(wanted);
        return card is null
            ? NotFound(catalog, wanted)
            : ToolResult.Text(Card(catalog, card));
    }

    /// <summary>
    ///     Composes the index: one line per top-level topic, with its summary.
    /// </summary>
    /// <param name="catalog">The pack's filtered catalog.</param>
    /// <param name="formats">The offered languages.</param>
    /// <returns>The index text.</returns>
    private static string Index(HelpCatalog catalog, IReadOnlyList<string> formats)
    {
        var roots = catalog.Roots;
        var width = roots.Count == 0 ? 0 : roots.Max(root => root.Id.Length) + 2;
        var text = new StringBuilder();
        text.Append("Diagram help topics for ").Append(string.Join(", ", formats)).Append(":\n\n");
        foreach (var root in roots)
        {
            text.Append(root.Id.PadRight(width)).Append(root.Summary).Append('\n');
        }

        text.Append('\n')
            .Append("Call ").Append(ToolName)
            .Append(" with a topic id for its card; each card lists the topics below and beside it.");
        return text.ToString();
    }

    /// <summary>
    ///     Composes one topic's card, followed by the full text of each of its example sources.
    /// </summary>
    /// <remarks>
    ///     The library's card text names its examples but does not carry them, and an example is the
    ///     quickest route to a source that draws, so each is appended in a fenced block.
    /// </remarks>
    /// <param name="catalog">The pack's filtered catalog.</param>
    /// <param name="card">The card.</param>
    /// <returns>The card text.</returns>
    private static string Card(HelpCatalog catalog, HelpCard card)
    {
        var text = new StringBuilder(catalog.RenderText(card).TrimEnd());
        if (card.Examples.Count > 0)
        {
            text.Append("\n\n## Example sources\n");
            foreach (var example in card.Examples)
            {
                var fence = Fence(example.Content);
                text.Append("\n### ").Append(example.Name).Append(" (").Append(example.FileName).Append(")\n\n");
                if (!string.IsNullOrWhiteSpace(example.Description))
                {
                    text.Append(example.Description.Trim()).Append("\n\n");
                }

                text.Append(fence).Append(example.Language).Append('\n')
                    .Append(example.Content.TrimEnd()).Append('\n')
                    .Append(fence).Append('\n');
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    ///     Chooses a code fence longer than any run of backticks in the content it encloses.
    /// </summary>
    /// <param name="content">The content to enclose.</param>
    /// <returns>The fence.</returns>
    private static string Fence(string content)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in content)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return new string('`', Math.Max(3, longest + 1));
    }

    /// <summary>
    ///     Refuses a topic the catalog does not serve, suggesting the served topics that contain it.
    /// </summary>
    /// <param name="catalog">The pack's filtered catalog.</param>
    /// <param name="topic">The trimmed topic.</param>
    /// <returns>The refusal.</returns>
    private static object NotFound(HelpCatalog catalog, string topic)
    {
        var echoed = topic.Length > MaxEchoedTopic ? topic[..MaxEchoedTopic] + "..." : topic;
        var lastSegment = topic.TrimEnd('/').Split('/')[^1];
        var suggestions = catalog.Cards
            .Select(card => card.Id)
            .Where(id => id.Contains(topic, StringComparison.OrdinalIgnoreCase)
                || (lastSegment.Length > 0 && id.Contains(lastSegment, StringComparison.OrdinalIgnoreCase)))
            .Take(MaxSuggestions)
            .ToList();

        return ToolResult.Denied(
            DenialReason.TargetNotFound,
            "No help topic '" + echoed + "'. "
            + (suggestions.Count > 0
                ? "Did you mean: " + string.Join(", ", suggestions) + "?"
                : "Call " + ToolName + " with no topic for the index."));
    }
}
