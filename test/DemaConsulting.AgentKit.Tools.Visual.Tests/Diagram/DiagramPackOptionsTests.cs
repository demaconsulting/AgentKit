using DemaConsulting.AgentKit.Tools.Visual.Diagram;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Diagram;

/// <summary>
///     Unit tests for the <see cref="DiagramPackOptions"/> record.
/// </summary>
public class DiagramPackOptionsTests
{
    /// <summary>
    ///     Proves the defaults offer every known language, in order, with the default rendering
    ///     bounds.
    /// </summary>
    [Fact]
    public void DiagramPackOptions_Default_OffersEveryLanguageWithTheDefaultBounds()
    {
        // Arrange / Act: read the defaults, and a fresh instance
        var defaults = DiagramPackOptions.Default;
        var fresh = new DiagramPackOptions();

        // Assert: all five languages, in order, and the default bounds
        Assert.Equal(["mermaid", "plantuml", "svg", "vega-lite", "openscad"], defaults.Formats);
        Assert.Same(VisualOptions.Default, defaults.Visual);
        Assert.Equal(defaults.Formats, fresh.Formats);
    }

    /// <summary>
    ///     Proves a format list is matched case-insensitively and stored in canonical form.
    /// </summary>
    [Fact]
    public void DiagramPackOptions_Formats_MixedCase_IsStoredCanonically()
    {
        // Arrange / Act: name two languages in mixed case with surrounding space
        var options = new DiagramPackOptions { Formats = [" Mermaid", "OPENSCAD "] };

        // Assert: stored in canonical form, in the given order
        Assert.Equal(["mermaid", "openscad"], options.Formats);
    }

    /// <summary>
    ///     Proves an empty list, an unknown language, a null name and a language listed twice are
    ///     each refused where they are set, and a null list too.
    /// </summary>
    [Fact]
    public void DiagramPackOptions_Formats_InvalidLists_AreRefused()
    {
        // Arrange / Act / Assert: each invalid list is refused when it is set
        Assert.Throws<ArgumentNullException>(() => new DiagramPackOptions { Formats = null! });
        var empty = Assert.Throws<ArgumentException>(() => new DiagramPackOptions { Formats = [] });
        var unknown = Assert.Throws<ArgumentException>(() => new DiagramPackOptions { Formats = ["graphviz"] });
        var nullName = Assert.Throws<ArgumentException>(() => new DiagramPackOptions { Formats = ["mermaid", null!] });
        var twice = Assert.Throws<ArgumentException>(() => new DiagramPackOptions { Formats = ["svg", "SVG"] });

        Assert.Equal(nameof(DiagramPackOptions.Formats), empty.ParamName);
        Assert.Contains("graphviz", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("mermaid, plantuml, svg, vega-lite, openscad", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("<null>", nullName.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", twice.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the assigned list is copied, so changing it afterwards changes nothing, and the
    ///     exposed list cannot be changed through itself.
    /// </summary>
    [Fact]
    public void DiagramPackOptions_Formats_AssignedList_IsCopiedAndExposedReadOnly()
    {
        // Arrange: a caller-owned array
        string[] formats = ["mermaid", "svg"];

        // Act: assign it, then change the caller's array
        var options = new DiagramPackOptions { Formats = formats };
        formats[0] = "plantuml";

        // Assert: the options kept their own copy, which is read-only
        Assert.Equal(["mermaid", "svg"], options.Formats);
        Assert.NotSame(formats, options.Formats);
        var list = Assert.IsAssignableFrom<IList<string>>(options.Formats);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = "svg");
    }

    /// <summary>
    ///     Proves the rendering bounds are applied as given, and a null value is refused.
    /// </summary>
    [Fact]
    public void DiagramPackOptions_Visual_IsAppliedAndNullIsRefused()
    {
        // Arrange: bounds other than the defaults
        var visual = new VisualOptions { MaxRenderSide = 1000 };

        // Act: assign them
        var options = new DiagramPackOptions { Visual = visual };

        // Assert: the same bounds are carried; null is refused
        Assert.Same(visual, options.Visual);
        Assert.Throws<ArgumentNullException>(() => new DiagramPackOptions { Visual = null! });
    }
}
