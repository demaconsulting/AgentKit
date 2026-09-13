using DemaConsulting.AgentKit.Tools.TextFile;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileLineBuffers"/> class.
/// </summary>
public class TextFileLineBuffersTests
{
    /// <summary>
    ///     Proves captured text is returned by a paste from the same slot, without consuming it.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_Capture_ThenPaste_ReturnsTheCapturedTextWithoutConsuming()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("slot", "captured");

        Assert.True(buffers.TryPaste("slot", out var first));
        Assert.Equal("captured", first);

        // Paste does not consume the slot, so a second paste succeeds too.
        Assert.True(buffers.TryPaste("slot", out var second));
        Assert.Equal("captured", second);
    }

    /// <summary>
    ///     Proves a later capture into the same slot replaces the earlier one.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_Recapture_ReplacesTheSlot()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("slot", "first");
        buffers.Capture("slot", "second");

        Assert.True(buffers.TryPaste("slot", out var text));
        Assert.Equal("second", text);
    }

    /// <summary>
    ///     Proves an empty slot is reported as a miss rather than as empty text.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_EmptySlot_IsAMiss()
    {
        var buffers = new TextFileLineBuffers();

        Assert.False(buffers.TryPaste("never-captured", out var text));
        Assert.Null(text);
    }

    /// <summary>
    ///     Proves distinct slots hold independent fragments.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_DistinctSlots_HoldIndependentFragments()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("a", "alpha");
        buffers.Capture("b", "beta");

        Assert.True(buffers.TryPaste("a", out var a));
        Assert.True(buffers.TryPaste("b", out var b));
        Assert.Equal("alpha", a);
        Assert.Equal("beta", b);
    }

    /// <summary>
    ///     Proves a null or empty slot name is a programming error, and a null capture is rejected.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_InvalidArguments_ThrowArgumentExceptions()
    {
        var buffers = new TextFileLineBuffers();

        Assert.Throws<ArgumentException>(() => buffers.Capture(string.Empty, "text"));
        Assert.Throws<ArgumentNullException>(() => buffers.Capture("slot", null!));
        Assert.Throws<ArgumentException>(() => buffers.TryPaste(string.Empty, out _));
    }

    /// <summary>
    ///     Proves the default slot name is published for the omitted-name case.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_DefaultSlot_IsPublished()
    {
        Assert.Equal("default", TextFileLineBuffers.DefaultSlot);
    }

    /// <summary>
    ///     Proves an empty store reports no populated slots.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_PopulatedSlots_Empty_IsEmpty()
    {
        var buffers = new TextFileLineBuffers();

        Assert.Empty(buffers.PopulatedSlots());
    }

    /// <summary>
    ///     Proves a single captured slot is reported by name.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_PopulatedSlots_OneSlot_NamesIt()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("only", "text");

        Assert.Equal(["only"], buffers.PopulatedSlots());
    }

    /// <summary>
    ///     Proves several captured slots are reported in ordinal order regardless of capture order.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_PopulatedSlots_SeveralSlots_AreOrderedOrdinally()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("gamma", "g");
        buffers.Capture("Alpha", "a");
        buffers.Capture("beta", "b");

        // Ordinal ordering places uppercase before lowercase, deterministically.
        Assert.Equal(["Alpha", "beta", "gamma"], buffers.PopulatedSlots());
    }

    /// <summary>
    ///     Proves a paste does not remove a slot, so the slot stays populated afterward — the
    ///     property the paste refusal relies on when it names slots that hold content.
    /// </summary>
    [Fact]
    public void TextFileLineBuffers_PopulatedSlots_AfterPaste_StillContainsTheSlot()
    {
        var buffers = new TextFileLineBuffers();
        buffers.Capture("slot", "captured");

        Assert.True(buffers.TryPaste("slot", out _));

        Assert.Equal(["slot"], buffers.PopulatedSlots());
    }
}
