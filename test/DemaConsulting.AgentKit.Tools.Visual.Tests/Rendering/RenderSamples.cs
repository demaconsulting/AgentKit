using System.IO.Compression;
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
    ///     Builds a one-slide PowerPoint deck whose slide holds a filled red rectangle.
    /// </summary>
    /// <remarks>
    ///     The slide's layout, master and theme are included because the library refuses a deck
    ///     without them; the shape uses no text, so the picture does not depend on installed fonts.
    /// </remarks>
    /// <returns>The PPTX bytes.</returns>
    internal static byte[] Pptx()
    {
        const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string Namespaces =
            "xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:r='" + Relationships + "' "
            + "xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main'";
        const string EmptyTree =
            "<p:nvGrpSpPr><p:cNvPr id='1' name=''/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>";

        return Package(
            ("[Content_Types].xml",
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Override PartName='/ppt/presentation.xml' ContentType='application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml'/>"
                + "<Override PartName='/ppt/slides/slide1.xml' ContentType='application/vnd.openxmlformats-officedocument.presentationml.slide+xml'/>"
                + "</Types>"),
            ("_rels/.rels", Rels("rId1", Relationships + "/officeDocument", "ppt/presentation.xml")),
            ("ppt/presentation.xml",
                "<p:presentation " + Namespaces + "><p:sldIdLst><p:sldId id='256' r:id='rId2'/></p:sldIdLst>"
                + "<p:sldSz cx='9144000' cy='6858000'/><p:notesSz cx='6858000' cy='9144000'/></p:presentation>"),
            ("ppt/_rels/presentation.xml.rels", Rels("rId2", Relationships + "/slide", "slides/slide1.xml")),
            ("ppt/slides/_rels/slide1.xml.rels", Rels("rId1", Relationships + "/slideLayout", "../slideLayouts/slideLayout1.xml")),
            ("ppt/slideLayouts/slideLayout1.xml",
                "<p:sldLayout " + Namespaces + "><p:cSld><p:spTree>" + EmptyTree + "</p:spTree></p:cSld></p:sldLayout>"),
            ("ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels("rId1", Relationships + "/slideMaster", "../slideMasters/slideMaster1.xml")),
            ("ppt/slideMasters/slideMaster1.xml",
                "<p:sldMaster " + Namespaces + "><p:cSld><p:spTree>" + EmptyTree + "</p:spTree></p:cSld>"
                + "<p:clrMap bg1='lt1' tx1='dk1' bg2='lt2' tx2='dk2' accent1='accent1' accent2='accent2' accent3='accent3' "
                + "accent4='accent4' accent5='accent5' accent6='accent6' hlink='hlink' folHlink='folHlink'/></p:sldMaster>"),
            ("ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels("rId1", Relationships + "/theme", "../theme/theme1.xml")),
            ("ppt/theme/theme1.xml", Theme()),
            ("ppt/slides/slide1.xml",
                "<p:sld " + Namespaces + "><p:cSld><p:spTree>" + EmptyTree
                + "<p:sp><p:nvSpPr><p:cNvPr id='2' name='Box'/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr>"
                + "<a:xfrm><a:off x='914400' y='914400'/><a:ext cx='4572000' cy='2743200'/></a:xfrm>"
                + "<a:prstGeom prst='rect'><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val='FF0000'/></a:solidFill>"
                + "</p:spPr></p:sp></p:spTree></p:cSld></p:sld>"));
    }

    /// <summary>
    ///     Builds a one-page Visio drawing whose page holds a filled red rectangle.
    /// </summary>
    /// <remarks>
    ///     The document uses the Visio 2012 namespace, the one the library reads; the shape has a
    ///     closed geometry and no text, so the picture does not depend on installed fonts.
    /// </remarks>
    /// <returns>The VSDX bytes.</returns>
    internal static byte[] Vsdx()
    {
        const string Visio = "http://schemas.microsoft.com/office/visio/2012/main";
        const string VisioRelationships = "http://schemas.microsoft.com/visio/2010/relationships";
        const string Namespaces =
            "xmlns='" + Visio + "' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'";

        // The rectangle's outline, corner to corner in inches, closed back at the origin.
        var corners = new[] { (0, 0), (4, 0), (4, 3), (0, 3), (0, 0) };
        var geometry = string.Concat(corners.Select((corner, index) =>
            $"<Row T='{(index == 0 ? "MoveTo" : "LineTo")}' IX='{index + 1}'>"
            + $"<Cell N='X' V='{corner.Item1}'/><Cell N='Y' V='{corner.Item2}'/></Row>"));

        return Package(
            ("[Content_Types].xml",
                "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'>"
                + "<Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/>"
                + "<Default Extension='xml' ContentType='application/xml'/>"
                + "<Override PartName='/visio/document.xml' ContentType='application/vnd.ms-visio.drawing.main+xml'/>"
                + "<Override PartName='/visio/pages/pages.xml' ContentType='application/vnd.ms-visio.pages+xml'/>"
                + "<Override PartName='/visio/pages/page1.xml' ContentType='application/vnd.ms-visio.page+xml'/>"
                + "</Types>"),
            ("_rels/.rels", Rels("rId1", VisioRelationships + "/document", "visio/document.xml")),
            ("visio/document.xml", "<VisioDocument " + Namespaces + "/>"),
            ("visio/_rels/document.xml.rels", Rels("rId1", VisioRelationships + "/pages", "pages/pages.xml")),
            ("visio/pages/pages.xml",
                "<Pages " + Namespaces + "><Page ID='0' NameU='Page-1' Name='Page-1'><PageSheet>"
                + "<Cell N='PageWidth' V='8.5'/><Cell N='PageHeight' V='11'/></PageSheet><Rel r:id='rId1'/></Page></Pages>"),
            ("visio/pages/_rels/pages.xml.rels", Rels("rId1", VisioRelationships + "/page", "page1.xml")),
            ("visio/pages/page1.xml",
                "<PageContents " + Namespaces + "><Shapes><Shape ID='1' Type='Shape'>"
                + "<Cell N='PinX' V='4.25'/><Cell N='PinY' V='5.5'/><Cell N='Width' V='4'/><Cell N='Height' V='3'/>"
                + "<Cell N='LocPinX' V='2'/><Cell N='LocPinY' V='1.5'/><Cell N='FillForegnd' V='#FF0000'/>"
                + "<Section N='Geometry' IX='0'>" + geometry + "</Section></Shape></Shapes></PageContents>"));
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

    /// <summary>
    ///     Builds an Open Packaging Conventions package from its parts.
    /// </summary>
    /// <param name="parts">Each part's name within the package and its XML content.</param>
    /// <returns>The package bytes.</returns>
    private static byte[] Package(params (string Name, string Content)[] parts)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write("<?xml version='1.0' encoding='UTF-8'?>");
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a relationships part holding one relationship.
    /// </summary>
    /// <param name="id">The relationship's identifier.</param>
    /// <param name="type">The relationship's type.</param>
    /// <param name="target">The part the relationship targets.</param>
    /// <returns>The relationships part's XML.</returns>
    private static string Rels(string id, string type, string target)
    {
        return "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>"
            + $"<Relationship Id='{id}' Type='{type}' Target='{target}'/></Relationships>";
    }

    /// <summary>
    ///     Builds the minimal complete theme a deck's slide master needs.
    /// </summary>
    /// <returns>The theme part's XML.</returns>
    private static string Theme()
    {
        static string Color(string name, string value) => $"<a:{name}><a:srgbClr val='{value}'/></a:{name}>";
        const string Fill = "<a:solidFill><a:schemeClr val='phClr'/></a:solidFill>";
        const string Font = "<a:latin typeface='Arial'/><a:ea typeface=''/><a:cs typeface=''/>";

        return "<a:theme xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' name='T'><a:themeElements>"
            + "<a:clrScheme name='C'>"
            + Color("dk1", "000000") + Color("lt1", "FFFFFF") + Color("dk2", "000000") + Color("lt2", "FFFFFF")
            + Color("accent1", "FF0000") + Color("accent2", "00FF00") + Color("accent3", "0000FF")
            + Color("accent4", "FFFF00") + Color("accent5", "00FFFF") + Color("accent6", "FF00FF")
            + Color("hlink", "0000FF") + Color("folHlink", "800080") + "</a:clrScheme>"
            + "<a:fontScheme name='F'><a:majorFont>" + Font + "</a:majorFont><a:minorFont>" + Font + "</a:minorFont></a:fontScheme>"
            + "<a:fmtScheme name='M'>"
            + "<a:fillStyleLst>" + Fill + Fill + Fill + "</a:fillStyleLst>"
            + "<a:lnStyleLst><a:ln w='6350'>" + Fill + "</a:ln><a:ln w='12700'>" + Fill + "</a:ln><a:ln w='19050'>" + Fill + "</a:ln></a:lnStyleLst>"
            + "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle>"
            + "<a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>"
            + "<a:bgFillStyleLst>" + Fill + Fill + Fill + "</a:bgFillStyleLst>"
            + "</a:fmtScheme></a:themeElements></a:theme>";
    }
}
