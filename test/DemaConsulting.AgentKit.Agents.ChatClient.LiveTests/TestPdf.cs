using System.Globalization;
using System.Text;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     Encodes the test PDF the live suite needs: one page whose left half is blue and whose right
///     half is yellow.
/// </summary>
/// <remarks>
///     Generated rather than checked in, so the suite carries no binary asset and the page's content
///     is stated in code: a model that has really seen the drawn page can name both colors and their
///     sides, and the file name reveals neither. The file is plain ASCII, so every byte offset the
///     cross-reference table records is a character offset.
/// </remarks>
internal static class TestPdf
{
    /// <summary>
    ///     Encodes a one-page, 400 by 200 point PDF: blue on the left half, yellow on the right.
    /// </summary>
    /// <returns>The PDF bytes.</returns>
    internal static byte[] SplitBlueYellow()
    {
        const string content = "0 0 1 rg 0 0 200 200 re f 1 1 0 rg 200 0 200 200 re f";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 200] /Contents 4 0 R >>",
            "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n" + content + "\nendstream"
        ];

        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(body.Length);
            body.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = body.Length;
        body.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        body.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(body.ToString());
    }
}
