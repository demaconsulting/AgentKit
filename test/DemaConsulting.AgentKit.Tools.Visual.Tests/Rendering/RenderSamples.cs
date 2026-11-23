using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Sources, documents and helpers the rendering tests draw and inspect.
/// </summary>
/// <remarks>
///     <para>
///     Every sample is small and deterministic, so a test can assert what was drawn without
///     comparing against a stored picture. The broken samples reproduce the failure cases the
///     rendering spike recorded for each format, so the shaping tests exercise the real library
///     rather than a hand-written exception.
///     </para>
///     <para>
///     The class is static and holds only constants and pure helpers, so it is safe for
///     concurrent use by tests running in parallel.
///     </para>
/// </remarks>
internal static class RenderSamples
{
    /// <summary>
    ///     A small Mermaid flowchart with readable labels.
    /// </summary>
    internal const string Mermaid = "flowchart LR\n  draft([Draft]) --> review{Review OK?}\n  review -- yes --> publish[Publish]\n";

    /// <summary>
    ///     A small PlantUML sequence diagram.
    /// </summary>
    internal const string PlantUml = "@startuml\nAlice -> Bob : hello\nBob --> Alice : hi\n@enduml\n";

    /// <summary>
    ///     A small Vega-Lite bar chart.
    /// </summary>
    internal const string VegaLite =
        "{\"width\":200,\"height\":120,\"data\":{\"values\":[{\"a\":\"x\",\"b\":3},{\"a\":\"y\",\"b\":5}]},"
        + "\"mark\":\"bar\",\"encoding\":{\"x\":{\"field\":\"a\",\"type\":\"nominal\"},"
        + "\"y\":{\"field\":\"b\",\"type\":\"quantitative\"}}}";

    /// <summary>
    ///     A small OpenSCAD part.
    /// </summary>
    internal const string Scad = "difference() {\n  cube([40, 20, 4]);\n  translate([10, 10, -1]) cylinder(h = 6, r = 3, $fn = 24);\n}\n";

    /// <summary>
    ///     A small SVG with a filled rectangle and a dark outline.
    /// </summary>
    internal const string Svg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\">"
        + "<rect x=\"10\" y=\"10\" width=\"180\" height=\"80\" fill=\"#336699\" stroke=\"#000000\"/></svg>";

    /// <summary>
    ///     A Mermaid flowchart whose arrow is malformed on line 2, column 5.
    /// </summary>
    internal const string MermaidBadArrow = "flowchart LR\n  A -> B\n";

    /// <summary>
    ///     A PlantUML sequence diagram whose statement on line 2 has no target.
    /// </summary>
    internal const string PlantUmlBadStatement = "@startuml\nAlice -> : hello\n@enduml\n";

    /// <summary>
    ///     A Vega-Lite specification naming an unknown mark, at column 41 of line 1.
    /// </summary>
    internal const string VegaLiteUnknownMark =
        "{ \"data\": {\"values\":[{\"a\":1}]}, \"mark\": \"bars\", \"encoding\": { \"x\": { \"field\": \"a\", \"type\": \"quantitative\" } } }";

    /// <summary>
    ///     An OpenSCAD model missing a closing parenthesis before line 2.
    /// </summary>
    internal const string ScadMissingParenthesis = "cube([1, 2, 3]\nsphere(5);\n";

    /// <summary>
    ///     An SVG that is not well-formed XML.
    /// </summary>
    internal const string SvgMalformed = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>";

    /// <summary>
    ///     A Mermaid diagram type the library does not draw, which it renders as an empty 16x16
    ///     square.
    /// </summary>
    internal const string MermaidUnsupportedType = "journey\n  title A day\n";

    /// <summary>
    ///     An SVG that draws nothing on a white page.
    /// </summary>
    internal const string SvgBlank = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"/>";

    /// <summary>
    ///     A 24x24 SVG icon: one filled circle.
    /// </summary>
    internal const string SvgIcon =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\">"
        + "<circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#000000\"/></svg>";

    /// <summary>
    ///     A 400x200 SVG with two filled circles.
    /// </summary>
    internal const string SvgTwoCircles =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"400\" height=\"200\">"
        + "<circle cx=\"100\" cy=\"100\" r=\"80\" fill=\"#000000\"/>"
        + "<circle cx=\"300\" cy=\"100\" r=\"80\" fill=\"#000000\"/></svg>";

    /// <summary>
    ///     A Mermaid flowchart of two nodes joined by an arrow.
    /// </summary>
    internal const string MermaidTwoNodes = "flowchart LR\n  a-->b\n";

    /// <summary>
    ///     A sphere the default triangle cap admits but a cap of 100 refuses.
    /// </summary>
    internal const string ScadSphere = "sphere(10, $fn = 64);\n";

    /// <summary>
    ///     Builds an SVG that draws one embedded picture.
    /// </summary>
    /// <param name="href">The reference the picture is embedded by.</param>
    /// <returns>The SVG source.</returns>
    internal static string SvgWithImage(string href)
    {
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"120\">"
            + "<rect width=\"200\" height=\"120\" fill=\"#ffffff\"/>"
            + "<rect x=\"2\" y=\"2\" width=\"20\" height=\"20\" fill=\"#000000\"/>"
            + "<image href=\"" + href + "\" x=\"10\" y=\"10\" width=\"180\" height=\"100\"/></svg>";
    }

    /// <summary>
    ///     Builds a PlantUML diagram that includes another file.
    /// </summary>
    /// <param name="href">The reference the file is included by.</param>
    /// <returns>The PlantUML source.</returns>
    internal static string PlantUmlInclude(string href)
    {
        return "@startuml\n!include " + href + "\nAlice -> Bob : hello\n@enduml\n";
    }

    /// <summary>
    ///     Builds an OpenSCAD model that includes another file.
    /// </summary>
    /// <param name="href">The reference the file is included by.</param>
    /// <returns>The OpenSCAD source.</returns>
    internal static string ScadInclude(string href)
    {
        return "include <" + href + ">\ncube(10);\n";
    }

    /// <summary>
    ///     Builds a Vega-Lite chart that reads its data from a file.
    /// </summary>
    /// <param name="href">The reference the data is read from.</param>
    /// <returns>The Vega-Lite specification.</returns>
    internal static string VegaLiteDataUrl(string href)
    {
        return "{\"width\":200,\"height\":120,\"data\":{\"url\":\"" + href.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"},"
            + "\"mark\":\"bar\",\"encoding\":{\"x\":{\"field\":\"a\",\"type\":\"nominal\"},"
            + "\"y\":{\"field\":\"b\",\"type\":\"quantitative\"}}}";
    }

    /// <summary>
    ///     Builds an OpenSCAD model that loops for tens of millions of evaluation steps without
    ///     drawing, so no render of it finishes within a fraction of a second; the final cube is
    ///     reached only if the loop completes.
    /// </summary>
    /// <returns>The OpenSCAD source.</returns>
    internal static string EndlessScad()
    {
        return "for (i = [0:9999]) for (j = [0:9999]) if (pow(sin(i * j), 2) + cos(i + j) < -5) cube(1);\n"
            + "cube(1);\n";
    }

    /// <summary>
    ///     Builds a PDF of the given number of pages, each a 300x200 point page holding a filled
    ///     rectangle.
    /// </summary>
    /// <param name="pages">The number of pages.</param>
    /// <returns>The PDF bytes.</returns>
    internal static byte[] Pdf(int pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', Enumerable.Range(0, pages).Select(page => (3 + (page * 2)) + " 0 R")) + "] /Count " + pages + " >>"
        };

        for (var page = 0; page < pages; page++)
        {
            var stream = "0 0 1 rg 20 20 " + (100 + (page * 50)) + " 100 re f";
            objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents " + (4 + (page * 2)) + " 0 R >>");
            objects.Add("<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream");
        }

        // Offsets are byte positions, so the body is plain ASCII throughout.
        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(body.Length);
            body.Append(index + 1).Append(" 0 obj").Append('\n').Append(objects[index]).Append('\n').Append("endobj").Append('\n');
        }

        var xref = body.Length;
        body.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        body.Append("trailer").Append('\n').Append("<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>").Append('\n')
            .Append("startxref").Append('\n').Append(xref).Append('\n').Append("%%EOF").Append('\n');
        return Encoding.ASCII.GetBytes(body.ToString());
    }

    /// <summary>
    ///     Builds a render request for an inline source.
    /// </summary>
    /// <param name="source">The source text.</param>
    /// <param name="fileName">The file name the source is drawn as.</param>
    /// <param name="policy">The access policy whose mapping confines references.</param>
    /// <param name="folder">The folder references resolve against, or null.</param>
    /// <returns>The request.</returns>
    internal static RenderRequest Request(string source, string fileName, PathPolicy policy, string? folder = null)
    {
        return new RenderRequest
        {
            Content = Encoding.UTF8.GetBytes(source),
            FileName = fileName,
            Folder = folder,
            Access = VisualAccess.ToFileAccess(policy),
            Limits = policy.Limits
        };
    }

    /// <summary>
    ///     Builds a policy granting read-write access to one folder, which is also the working
    ///     directory.
    /// </summary>
    /// <param name="root">The folder.</param>
    /// <returns>The policy.</returns>
    internal static PathPolicy Policy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Decodes a PNG a render returned.
    /// </summary>
    /// <param name="png">The PNG bytes.</param>
    /// <returns>The decoded picture; the caller disposes it.</returns>
    internal static Surface Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return PngCodec.Load(stream);
    }
}
