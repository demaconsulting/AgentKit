using System.Text.Json;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputStore"/> class.
/// </summary>
public class ToolOutputStoreTests
{
    /// <summary>
    ///     Proves the store issues opaque, monotonic identifiers and returns the output it retained.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_Outputs_IssuesMonotonicIdsAndRetainsTheLines()
    {
        // Arrange: a store with the default ceilings
        var store = new ToolOutputStore(ToolOutputOptions.Default);

        // Act: retain two outputs and look the first one up
        var first = store.Add("alpha\nbeta");
        var second = store.Add("gamma");
        var outcome = store.Find("output-1", out var found);

        // Assert: identifiers in issue order, and the first output's lines are intact
        Assert.Equal("output-1", first!.Id);
        Assert.Equal("output-2", second!.Id);
        Assert.Equal(ToolOutputLookup.Found, outcome);
        Assert.Equal(["alpha", "beta"], found!.Lines);
    }

    /// <summary>
    ///     Proves every line ending is normalized and an empty output is one empty line.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_MixedLineEndings_AreNormalizedToLines()
    {
        // Arrange: a store
        var store = new ToolOutputStore(ToolOutputOptions.Default);

        // Act: retain an output with CRLF, CR and LF endings, one ending in a break, and an empty one
        var mixed = store.Add("one\r\ntwo\rthree\nfour");
        var terminated = store.Add("one\ntwo\n");
        var empty = store.Add(string.Empty);

        // Assert: one line per ending, a final break opens no empty line, and the size counts one
        // character per line break
        Assert.Equal(["one", "two", "three", "four"], mixed!.Lines);
        Assert.Equal("one\ntwo\nthree\nfour".Length, mixed.Characters);
        Assert.Equal(["one", "two"], terminated!.Lines);
        Assert.Equal([string.Empty], empty!.Lines);
    }

    /// <summary>
    ///     Proves a physical line longer than the wrap width becomes several numbered lines and the
    ///     entry records that it was wrapped.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_VeryLongLine_IsWrappedOntoContinuationLines()
    {
        // Arrange: a store and one line two and a half times the wrap width
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        var line = new string('x', ToolOutputStore.MaxLineCharacters * 5 / 2);

        // Act: retain it
        var entry = store.Add(line);

        // Assert: three lines, none longer than the wrap width, and nothing lost
        Assert.True(entry!.Wrapped);
        Assert.Equal(3, entry.Lines.Count);
        Assert.All(entry.Lines, part => Assert.True(part.Length <= ToolOutputStore.MaxLineCharacters));
        Assert.Equal(line, string.Concat(entry.Lines));
    }

    /// <summary>
    ///     Proves wrapping never separates the two halves of a surrogate pair.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_WrapAtSurrogatePair_KeepsThePairTogether()
    {
        // Arrange: a line whose wrap boundary falls inside a surrogate pair
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        var line = new string('x', ToolOutputStore.MaxLineCharacters - 1) + "\U0001F600" + "tail";

        // Act: retain it
        var entry = store.Add(line);

        // Assert: the first line stops before the pair and the pair opens the second line
        Assert.Equal(ToolOutputStore.MaxLineCharacters - 1, entry!.Lines[0].Length);
        Assert.StartsWith("\U0001F600", entry.Lines[1], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a structured document is indented so it spans lines a model can page through.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Indent_CompactJson_SpansSeveralLines()
    {
        // Arrange: a compact JSON document on one line, carrying non-ASCII text
        using var document = JsonDocument.Parse("{\"name\":\"café <b>\",\"items\":[1,2,3]}");

        // Act: indent it
        var indented = ToolOutputStore.Indent(document.RootElement);

        // Assert: several lines, still readable rather than escaped
        Assert.True(indented.Split('\n').Length > 3);
        Assert.Contains("café <b>", indented, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an output larger than the per-output ceiling is not retained and evicts nothing.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_OutputOverPerOutputCeiling_IsNotRetained()
    {
        // Arrange: a store with a small per-output ceiling, already holding one output
        var store = new ToolOutputStore(new ToolOutputOptions(maxOutputCharacters: 10, maxRetainedCharacters: 10));
        store.Add("held");

        // Act: offer an output above the ceiling
        var refused = store.Add(new string('x', 11));

        // Assert: nothing retained, and the earlier output survives
        Assert.Null(refused);
        Assert.Equal(ToolOutputLookup.Found, store.Find("output-1", out _));
    }

    /// <summary>
    ///     Proves an output whose raw length is exactly at the early-refusal bound is still
    ///     retained when it fits once normalized.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_OutputAtTheRawLengthBound_IsRetainedWhenItFits()
    {
        // Arrange: ceilings of one and three characters
        var one = new ToolOutputStore(new ToolOutputOptions(maxOutputCharacters: 1, maxRetainedCharacters: 1));
        var three = new ToolOutputStore(new ToolOutputOptions(maxOutputCharacters: 3, maxRetainedCharacters: 3));

        // Act: offer outputs of nothing but CRLF pairs, twice the ceiling plus two characters long
        var atOne = one.Add("\r\n\r\n");
        var atThree = three.Add(string.Concat(Enumerable.Repeat("\r\n", 4)));

        // Assert: both are retained, each normalized to exactly its ceiling
        Assert.NotNull(atOne);
        Assert.Equal(1, atOne.Characters);
        Assert.NotNull(atThree);
        Assert.Equal(3, atThree.Characters);
    }

    /// <summary>
    ///     Proves an output beyond the early-refusal bound is refused and evicts nothing.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_OutputBeyondTheRawLengthBound_IsRefusedAndEvictsNothing()
    {
        // Arrange: a one-character store already holding one output
        var store = new ToolOutputStore(new ToolOutputOptions(maxOutputCharacters: 1, maxRetainedCharacters: 1));
        store.Add("a");

        // Act: offer an output one character beyond the bound
        var refused = store.Add("x\r\n\r\n");

        // Assert: nothing retained, and the earlier output survives
        Assert.Null(refused);
        Assert.Equal(ToolOutputLookup.Found, store.Find("output-1", out _));
    }

    /// <summary>
    ///     Proves exhaustively over short outputs that the early refusal never refuses an output that
    ///     would fit, and that its bound is tight.
    /// </summary>
    [Fact]
    public void ToolOutputStore_MustExceed_NeverRefusesAnOutputThatFits()
    {
        // Arrange: every output of up to eight characters drawn from a letter and both break characters
        char[] alphabet = ['a', '\r', '\n'];
        var outputs = new List<string> { string.Empty };
        var previous = new List<string> { string.Empty };
        for (var length = 1; length <= 8; length++)
        {
            previous = [.. previous.SelectMany(prefix => alphabet.Select(c => prefix + c))];
            outputs.AddRange(previous);
        }

        for (var max = 1; max <= 5; max++)
        {
            // Act & Assert: wherever the raw length refuses, normalization would have refused too
            foreach (var output in outputs.Where(output => ToolOutputStore.MustExceed(output.Length, max)))
            {
                ToolOutputStore.Normalize(output, out var characters, out _);
                Assert.True(characters > max, $"'{output.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}' fits a ceiling of {max} but was refused.");
            }

            // Act & Assert: the longest output the bound admits can fit exactly
            var tightest = string.Concat(Enumerable.Repeat("\r\n", max + 1));
            ToolOutputStore.Normalize(tightest, out var tightCharacters, out _);
            Assert.False(ToolOutputStore.MustExceed(tightest.Length, max));
            Assert.Equal(max, tightCharacters);
        }
    }

    /// <summary>
    ///     Proves measuring an output without copying it gives exactly the size normalization
    ///     reports, across every line ending, a trailing break, wrapping and a wrap at a surrogate
    ///     pair, and indented structured data.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Measure_EqualsTheNormalizedSize()
    {
        // Arrange: outputs exercising every rule normalization applies
        using var document = JsonDocument.Parse("{\"name\":\"café <b>\",\"items\":[1,2,3]}");
        string[] outputs =
        [
            string.Empty,
            "a",
            "a\n",
            "a\r\nb\rc\n",
            "\r\n\r\n",
            new string('x', 4_500),
            new string('x', ToolOutputStore.MaxLineCharacters - 1) + "\U0001F600" + "tail",
            ToolOutputStore.Indent(document.RootElement),
        ];

        foreach (var output in outputs)
        {
            // Act: normalize and measure
            ToolOutputStore.Normalize(output, out var characters, out _);

            // Assert: the same size
            Assert.Equal(characters, ToolOutputStore.Measure(output));
        }
    }

    /// <summary>
    ///     Proves the oldest outputs are evicted first when the count ceiling would be exceeded.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_BeyondTheCountCeiling_EvictsTheOldestFirst()
    {
        // Arrange: a store holding at most two outputs
        var store = new ToolOutputStore(new ToolOutputOptions(maxRetainedOutputs: 2));

        // Act: retain three
        store.Add("one");
        store.Add("two");
        store.Add("three");

        // Assert: the first is gone and the later two remain
        Assert.Equal(ToolOutputLookup.Evicted, store.Find("output-1", out _));
        Assert.Equal(ToolOutputLookup.Found, store.Find("output-2", out _));
        Assert.Equal(ToolOutputLookup.Found, store.Find("output-3", out _));
    }

    /// <summary>
    ///     Proves the oldest outputs are evicted first when the character ceiling would be exceeded.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_BeyondTheCharacterCeiling_EvictsTheOldestFirst()
    {
        // Arrange: a store holding at most ten characters in total
        var store = new ToolOutputStore(new ToolOutputOptions(maxOutputCharacters: 10, maxRetainedCharacters: 10));

        // Act: retain two six-character outputs
        store.Add("aaaaaa");
        store.Add("bbbbbb");

        // Assert: the first was evicted to make room for the second
        Assert.Equal(ToolOutputLookup.Evicted, store.Find("output-1", out _));
        Assert.Equal(ToolOutputLookup.Found, store.Find("output-2", out _));
    }

    /// <summary>
    ///     Proves a lookup distinguishes a malformed identifier from one never issued.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Find_MalformedOrUnissuedIds_AreDistinguished()
    {
        // Arrange: a store holding one output
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        store.Add("held");

        // Act / Assert: malformed forms, and well-formed identifiers that were never issued
        Assert.Equal(ToolOutputLookup.Malformed, store.Find(null, out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("notes.txt", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-01", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find(string.Empty, out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("   ", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("OUTPUT-1", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-0", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-+1", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output--1", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output- 1", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-1.0", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-\u0661", out _));
        Assert.Equal(ToolOutputLookup.Malformed, store.Find("output-9223372036854775808", out _));
        Assert.Equal(ToolOutputLookup.Unknown, store.Find("output-2", out _));
        Assert.Equal(ToolOutputLookup.Unknown, store.Find("output-9223372036854775807", out _));
        Assert.Equal(ToolOutputLookup.Found, store.Find(" output-1 ", out _));
    }

    /// <summary>
    ///     Proves each lookup failure is refused with a statement of what is so.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Refusal_EachFailure_StatesItsFact()
    {
        // Act: compose a refusal for each failure
        var malformed = Assert.IsType<string>(ToolOutputStore.Refusal(ToolOutputLookup.Malformed, "x"));
        var unknown = Assert.IsType<string>(ToolOutputStore.Refusal(ToolOutputLookup.Unknown, "output-9"));
        var evicted = Assert.IsType<string>(ToolOutputStore.Refusal(ToolOutputLookup.Evicted, "output-3"));

        // Assert: the reason and the fact each states
        Assert.StartsWith("Denied (InvalidRequest)", malformed, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", unknown, StringComparison.Ordinal);
        Assert.Contains("never retained", unknown, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", evicted, StringComparison.Ordinal);
        Assert.Contains("no longer retained", evicted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a malformed identifier longer than any real one is not echoed back, so the model
    ///     cannot choose a refusal's length, while a short near miss still is.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Refusal_LongMalformedId_IsNotEchoed()
    {
        // Arrange: a very long value, and a near miss exactly at the echo limit
        var longValue = new string('q', 5_000);
        var nearMiss = new string('q', ToolOutputStore.MaxEchoedIdCharacters);

        // Act: compose the malformed-identifier refusal for each
        var longRefusal = Assert.IsType<string>(ToolOutputStore.Refusal(ToolOutputLookup.Malformed, longValue));
        var nearRefusal = Assert.IsType<string>(ToolOutputStore.Refusal(ToolOutputLookup.Malformed, nearMiss));

        // Assert: the long value is absent and the refusal is short; the near miss is echoed
        Assert.StartsWith("Denied (InvalidRequest)", longRefusal, StringComparison.Ordinal);
        Assert.DoesNotContain("qqqq", longRefusal, StringComparison.Ordinal);
        Assert.True(longRefusal.Length < 200, $"The refusal is {longRefusal.Length} characters.");
        Assert.Contains("'" + nearMiss + "'", nearRefusal, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves concurrent additions each receive a distinct identifier and every one is
    ///     retrievable.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Add_Concurrently_IssuesDistinctIdsAndRetainsEveryOutput()
    {
        // Arrange: a store large enough to hold every output
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        var ids = new string[200];

        // Act: add from many threads at once
        Parallel.For(0, ids.Length, index => ids[index] = store.Add("output " + index)!.Id);

        // Assert: every identifier is distinct and every output is still held
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(ToolOutputLookup.Found, store.Find(id, out _)));
    }

    /// <summary>
    ///     Proves a snapshot restored into a new store keeps every identifier, its lines and the
    ///     order of eviction, and numbering continues after the last identifier issued.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Snapshot_Restore_PreservesIdsAndOrder()
    {
        // Arrange: a store that retained three outputs and evicted the first
        var original = new ToolOutputStore(new ToolOutputOptions(maxRetainedOutputs: 2));
        original.Add("one");
        original.Add("two\nlines");
        original.Add("three");
        var entries = original.Snapshot(out var lastIssued);

        // Act: restore into a new store and retain one more output
        var restored = new ToolOutputStore(new ToolOutputOptions(maxRetainedOutputs: 2));
        restored.Restore(
            entries.Select(entry => (long.Parse(entry.Id["output-".Length..], System.Globalization.CultureInfo.InvariantCulture), entry.Lines, entry.Wrapped)),
            lastIssued);
        var next = restored.Add("four");

        // Assert: the snapshot listed the two held outputs oldest first under the issued counter,
        // the restored store answers them, and the next output is numbered after the last one issued
        Assert.Equal(3, lastIssued);
        Assert.Equal(["output-2", "output-3"], entries.Select(entry => entry.Id));
        Assert.Equal("output-4", next!.Id);
        Assert.Equal(ToolOutputLookup.Evicted, restored.Find("output-1", out _));
        Assert.Equal(ToolOutputLookup.Evicted, restored.Find("output-2", out _));
        Assert.Equal(ToolOutputLookup.Found, restored.Find("output-3", out var three));
        Assert.Equal(["three"], three!.Lines);
    }

    /// <summary>
    ///     Proves a store that already retains output cannot be restored over.
    /// </summary>
    [Fact]
    public void ToolOutputStore_Restore_NonEmptyStore_ThrowsInvalidOperationException()
    {
        // Arrange: a store that has retained an output
        var store = new ToolOutputStore(ToolOutputOptions.Default);
        store.Add("held");

        // Act / Assert: restoring would interleave two histories under one counter
        Assert.Throws<InvalidOperationException>(() => store.Restore([], 0));
    }
}
