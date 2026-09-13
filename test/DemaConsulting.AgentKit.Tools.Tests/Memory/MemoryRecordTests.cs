using DemaConsulting.AgentKit.Tools.Memory;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryRecord"/> and <see cref="MemoryMatch"/> records.
/// </summary>
public class MemoryRecordTests
{
    /// <summary>
    ///     Proves a memory carries every part the family needs — identifier, descriptor, details,
    ///     both provenance fields and the vector — and hands each of them back unchanged.
    /// </summary>
    [Fact]
    public void MemoryRecord_Constructor_StatedValues_AreHeldUnchanged()
    {
        // Arrange / Act: one memory as the file tool would construct it
        var vector = new float[] { 0.5f, 0.25f };
        var memory = new MemoryRecord(
            "mem-1a2b3c4d",
            "The rear tyre pressure for the touring model.",
            "18 psi cold, measured at the valve, for loads up to 120 kg.",
            "02-revision.md",
            "Section 4.2",
            vector);

        // Assert: every part is the part that was stated
        Assert.Equal("mem-1a2b3c4d", memory.Id);
        Assert.Equal("The rear tyre pressure for the touring model.", memory.Descriptor);
        Assert.Equal("18 psi cold, measured at the valve, for loads up to 120 kg.", memory.Details);
        Assert.Equal("02-revision.md", memory.SourceDocument);
        Assert.Equal("Section 4.2", memory.SourceLocator);
        Assert.Equal(vector, memory.Embedding.ToArray());
    }

    /// <summary>
    ///     Proves a memory whose source is unknown is a legitimate memory rather than a malformed
    ///     one, so that a model never has to invent a citation to file what it read.
    /// </summary>
    [Fact]
    public void MemoryRecord_Constructor_AbsentProvenance_IsPermitted()
    {
        // Arrange / Act: a memory with no stated source
        var memory = new MemoryRecord("mem-1", "A descriptor.", "Some details.", null, null, new float[] { 1.0f });

        // Assert: the absence is held as an absence rather than rejected or invented
        Assert.Null(memory.SourceDocument);
        Assert.Null(memory.SourceLocator);
    }

    /// <summary>
    ///     Proves a memory can be re-stated with one part changed and everything else carried
    ///     across, which is how an update replaces details without disturbing the vector.
    /// </summary>
    [Fact]
    public void MemoryRecord_With_ReplacesOnePartAndKeepsTheRest()
    {
        // Arrange: a stored memory
        var original = new MemoryRecord("mem-1", "A descriptor.", "Old details.", "a.md", "§1", new float[] { 1.0f });

        // Act: replace the details alone
        var updated = original with { Details = "New details." };

        // Assert: only the details changed, and the vector is the same one
        Assert.Equal("New details.", updated.Details);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.Descriptor, updated.Descriptor);
        Assert.Equal(original.SourceDocument, updated.SourceDocument);
        Assert.Equal(original.SourceLocator, updated.SourceLocator);
        Assert.Equal(original.Embedding.ToArray(), updated.Embedding.ToArray());
    }

    /// <summary>
    ///     Proves a match carries the memory and the score of one comparison side by side, so the
    ///     score is never mistaken for a property of the memory itself.
    /// </summary>
    [Fact]
    public void MemoryMatch_Constructor_CarriesTheMemoryAndItsSimilarity()
    {
        // Arrange: one memory
        var memory = new MemoryRecord("mem-1", "A descriptor.", "Some details.", null, null, new float[] { 1.0f });

        // Act: the same memory scored against two different queries
        var close = new MemoryMatch(memory, 0.965);
        var distant = new MemoryMatch(memory, 0.12);

        // Assert: the memory is one thing and the score is another
        Assert.Same(memory, close.Memory);
        Assert.Same(memory, distant.Memory);
        Assert.Equal(0.965, close.Similarity);
        Assert.Equal(0.12, distant.Similarity);
    }
}
