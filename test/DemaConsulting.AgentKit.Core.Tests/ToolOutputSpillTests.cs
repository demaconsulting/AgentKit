using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputSpill"/> class.
/// </summary>
/// <remarks>
///     Tool delegates are declared <c>Task&lt;object&gt;</c> deliberately, matching every real
///     guarded tool; see the remarks on <see cref="GuardedToolFactoryTests"/>.
/// </remarks>
public class ToolOutputSpillTests
{
    /// <summary>
    ///     The result ceiling most scenarios compose against, small enough to exceed cheaply.
    /// </summary>
    private const int Ceiling = 1_000;

    /// <summary>
    ///     Proves a result within the ceiling is returned exactly as the tool produced it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ResultWithinCeiling_IsReturnedUnchanged()
    {
        // Arrange: a tool returning text shorter than the ceiling
        var tools = ToolOutputSpill.Apply([TextTool("probe_small", "short answer")], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_small");

        // Assert: untouched
        Assert.Equal("short answer", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves an oversized text result is replaced by a preview naming its reference, its size,
    ///     and the reader tools, bounded by the ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedText_ReturnsBoundedPreviewNamingReferenceAndReaders()
    {
        // Arrange: a tool returning far more than the ceiling
        var big = Lines(500);
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", big)], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: a successful result naming the reference, the source, the size and both readers,
        // within the ceiling
        Assert.StartsWith("[Output output-1 from probe_big: " + big.Length + " characters in 500 lines", preview, StringComparison.Ordinal);
        Assert.Contains(ToolOutputReadTool.ToolName, preview, StringComparison.Ordinal);
        Assert.Contains(ToolOutputSearchTool.ToolName, preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Denied", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= Ceiling, $"The preview is {preview.Length} characters.");
    }

    /// <summary>
    ///     Proves the preview shows numbered lines from the head and the tail of the output, with the
    ///     omitted range marked between them.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedText_PreviewShowsNumberedHeadAndTail()
    {
        // Arrange: a 500-line output
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: the first and last lines, numbered and padded like text_file_read, and a marker
        Assert.Contains("\n  1| line 1\n", preview, StringComparison.Ordinal);
        Assert.EndsWith("\n500| line 500", preview, StringComparison.Ordinal);
        Assert.Contains("omitted ...]", preview, StringComparison.Ordinal);
        Assert.Contains("Showing lines 1-", preview, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves structured data is retained indented, so it can be paged line by line.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedStructuredResult_IsRetainedIndented()
    {
        // Arrange: a tool returning a large structured result, which reaches the runtime as JSON
        var rows = Enumerable.Range(1, 200).Select(n => new { row = n, name = "item " + n }).ToList();
        var tool = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Structured(rows))),
            "probe_rows",
            "Returns many rows.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it and read the start of what was retained
        await InvokeAsync(tools, "probe_rows");
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 6,
        }));

        // Assert: one value per line, not one compact line
        Assert.StartsWith("output-1 lines 1-6 of ", page, StringComparison.Ordinal);
        Assert.Contains("\"row\": 1", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a sequence of text parts is retained as text.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedTextContentList_IsRetained()
    {
        // Arrange: a tool returning two large text parts
        var tool = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult<object>(
                new List<AIContent> { new TextContent(Lines(100)), new TextContent(Lines(100)) })),
            "probe_parts",
            "Returns text parts.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_parts");

        // Assert: a preview over 200 lines
        Assert.Contains("in 200 lines", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves binary content is never retained, even beside a caption that alone exceeds the
    ///     ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_BinaryContent_PassesThroughUnchanged()
    {
        // Arrange: a bare image and a captioned image whose caption exceeds the ceiling
        var bytes = new byte[Ceiling * 4];
        var bare = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Image(bytes, "image/png"))),
            "probe_image",
            "Returns an image.");
        var captioned = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Image(bytes, "image/png", Lines(500)))),
            "probe_captioned",
            "Returns a captioned image.");
        var tools = ToolOutputSpill.Apply([bare, captioned], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke both
        var bareResult = await InvokeAsync(tools, "probe_image");
        var captionedResult = await InvokeAsync(tools, "probe_captioned");

        // Assert: the content arrives as the tool produced it
        Assert.IsType<DataContent>(bareResult);
        var parts = Assert.IsAssignableFrom<IList<AIContent>>(captionedResult);
        Assert.IsType<DataContent>(parts[1]);
    }

    /// <summary>
    ///     Proves a tool that returns nothing still returns nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_NullResult_PassesThrough()
    {
        // Arrange: a tool returning nothing
        var tool = GuardedToolFactory.Create(
            (Func<Task<object?>>)(() => Task.FromResult<object?>(null)),
            "probe_empty",
            "Returns nothing.");
        var tools = ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: Ceiling));

        // Act / Assert: an absence, not a reference
        Assert.Null(await InvokeAsync(tools, "probe_empty"));
    }

    /// <summary>
    ///     Proves a tool that declared its output oversized is retained, whatever the measured size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedMarker_IsRetained()
    {
        // Arrange: a tool declaring a modest output oversized, as a tool with a tighter ceiling would
        var tools = ToolOutputSpill.Apply(
            [MarkerTool("probe_marker", "alpha\nbeta", "The listing exceeds the limit.")],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = Assert.IsType<string>(await InvokeAsync(tools, "probe_marker"));

        // Assert: a reference, not the refusal, and no claim that ten characters exceed the
        // 1,000-character ceiling - the header names the tool's declaration instead
        Assert.StartsWith("[Output output-1 from probe_marker", result, StringComparison.Ordinal);
        Assert.Contains("which its tool reported as too large to return", result, StringComparison.Ordinal);
        Assert.DoesNotContain("larger than", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a declared-oversized output too large even to retain is refused with the tool's own
    ///     refusal, exactly as without retention.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_MarkerOverRetentionCeiling_ReturnsTheToolsRefusal()
    {
        // Arrange: a per-output ceiling below the output's size
        const string message = "The listing exceeds the 1000-character result limit.";
        var tools = ToolOutputSpill.Apply(
            [MarkerTool("probe_marker", Lines(500), message)],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(maxOutputCharacters: 2_000, maxRetainedCharacters: 2_000));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_marker");

        // Assert: byte-for-byte the refusal the tool always gave
        Assert.Equal(ToolResult.Denied(DenialReason.ResourceTooLarge, message), result);
    }

    /// <summary>
    ///     Proves any other output too large to retain is refused naming the retention ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_PlainOutputOverRetentionCeiling_IsRefusedNamingTheCeiling()
    {
        // Arrange: a per-output ceiling below the output's size
        var tools = ToolOutputSpill.Apply(
            [TextTool("probe_big", Lines(500))],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(maxOutputCharacters: 2_000, maxRetainedCharacters: 2_000));

        // Act: invoke it
        var result = Assert.IsType<string>(await InvokeAsync(tools, "probe_big"));

        // Assert: a refusal naming the tool and the retention ceiling, never a truncation
        Assert.StartsWith("Denied (ResourceTooLarge): The output of 'probe_big'", result, StringComparison.Ordinal);
        Assert.Contains("2000-character retention limit", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refusal over the retention ceiling states the size the output would be retained
    ///     at - the size the ceiling is measured on - not the smaller size the tool returned.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OutputOverRetentionCeilingOnceRetained_ReportsTheRetainedSize()
    {
        // Arrange: compact JSON of 3,894 characters that indents to 6,895, and one 5,000-character
        // line that wraps to 5,002, against a 5,000-character retention ceiling
        using var document = JsonDocument.Parse("[" + string.Join(",", Enumerable.Range(1, 1_000)) + "]");
        var compact = new FixedFunction("probe_compact", document.RootElement.Clone());
        var tools = ToolOutputSpill.Apply(
            [compact, TextTool("probe_line", new string('x', 5_000))],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(maxOutputCharacters: 5_000, maxRetainedCharacters: 5_000));

        // Act: invoke both
        var json = Assert.IsType<string>(await InvokeAsync(tools, "probe_compact"));
        var line = Assert.IsType<string>(await InvokeAsync(tools, "probe_line"));

        // Assert: each refusal states a size that does exceed the limit it names
        Assert.Contains("is 6895 characters", json, StringComparison.Ordinal);
        Assert.DoesNotContain("3894", json, StringComparison.Ordinal);
        Assert.Contains("5000-character retention limit", json, StringComparison.Ordinal);
        Assert.Contains("is 5002 characters", line, StringComparison.Ordinal);
        Assert.Contains("5000-character retention limit", line, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a single-pass sequence of content is enumerated exactly once, so the parts reach
    ///     the runtime intact whether text alone or beside an image, and that a list is still
    ///     delivered as the very instance the tool returned.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_SinglePassSequence_IsEnumeratedOnce()
    {
        // Arrange: single-pass sequences that throw when enumerated twice, and a list
        var image = new DataContent(new byte[16], "image/png");
        var listed = new List<AIContent> { new TextContent("listed") };
        var tools = ToolOutputSpill.Apply(
            [
                ContentTool("probe_lazy_text", () => new SinglePassSequence(new TextContent("small"))),
                ContentTool("probe_lazy_image", () => new SinglePassSequence(new TextContent("caption"), image)),
                ContentTool("probe_list", () => listed),
            ],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke each and enumerate what was delivered
        var text = Assert.IsAssignableFrom<IEnumerable<AIContent>>(await InvokeAsync(tools, "probe_lazy_text")).ToList();
        var mixed = Assert.IsAssignableFrom<IEnumerable<AIContent>>(await InvokeAsync(tools, "probe_lazy_image")).ToList();
        var list = await InvokeAsync(tools, "probe_list");

        // Assert: every part delivered, in order, and the list untouched
        Assert.Equal("small", Assert.IsType<TextContent>(Assert.Single(text)).Text);
        Assert.Equal(2, mixed.Count);
        Assert.Equal("caption", Assert.IsType<TextContent>(mixed[0]).Text);
        Assert.Same(image, mixed[1]);
        Assert.Same(listed, list);
    }

    /// <summary>
    ///     Proves a JSON string is measured by its JSON text, quotes and escapes included, and is
    ///     retained as its decoded text so its lines can be paged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_JsonString_IsMeasuredByItsJsonTextAndRetainedDecoded()
    {
        // Arrange: a JSON string of 400 lines - 800 characters decoded, 1,202 as JSON text
        using var document = JsonDocument.Parse("\"" + string.Concat(Enumerable.Repeat("a\\n", 400)) + "\"");
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_json", document.RootElement.Clone())],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it and read the first two retained lines
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_json"));
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 2,
        }));

        // Assert: retained because its JSON text exceeds the ceiling, and paged as decoded lines
        Assert.StartsWith("[Output output-1 from probe_json: 1202 characters in 400 lines", preview, StringComparison.Ordinal);
        Assert.Contains("1| a\n", page, StringComparison.Ordinal);
        Assert.Contains("2| a", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an undefined JSON element, which carries no value, reaches the runtime exactly as
    ///     it would without retention.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_UndefinedJsonElement_PassesThrough()
    {
        // Arrange: a hand-built function returning an undefined element
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_undefined", default(JsonElement))],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_undefined");

        // Assert: the undefined element itself, no exception
        Assert.Equal(JsonValueKind.Undefined, Assert.IsType<JsonElement>(result).ValueKind);
    }

    /// <summary>
    ///     Proves a structured object a hand-built function returns as it is - never serialized by
    ///     the guarded factory - is measured by its JSON and retained when that exceeds the ceiling.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_OversizedStructuredObjectFromHandBuiltFunction_IsRetained()
    {
        // Arrange: a hand-built function returning an anonymous object whose JSON exceeds the ceiling
        var value = new { Items = Enumerable.Range(1, 200).Select(n => "item " + n).ToArray() };
        var expected = JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType()));
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_object", value)],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it and read the first three retained lines
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_object"));
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 3,
        }));

        // Assert: retained, sized by its JSON text, and paged one indented value per line
        Assert.StartsWith(
            "[Output output-1 from probe_object: " + expected.GetRawText().Length + " characters",
            preview,
            StringComparison.Ordinal);
        Assert.Contains("\"items\": [", page, StringComparison.Ordinal);
        Assert.Contains("\"item 1\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a structured result a hand-built function returns within the ceiling is delivered
    ///     as the very JSON a guarded-factory tool returning the same value delivers.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_StructuredResultFromHandBuiltFunction_IsDeliveredAsTheJsonTheGuardedFactoryDelivers()
    {
        // Arrange: the same small record from a composed hand-built function and a factory-built tool
        var value = new Sample("a", 3);
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_small", value)],
            new ToolLimits(maxResultCharacters: Ceiling));
        var factoryBuilt = GuardedToolFactory.Create((Func<object>)(() => value), "probe_small", "Returns a record.");

        // Act: invoke both
        var handBuilt = await InvokeAsync(tools, "probe_small");
        var fromFactory = await factoryBuilt.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: a JSON element serialized with the default options, identical to the factory's
        var expected = JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(Sample)));
        var element = Assert.IsType<JsonElement>(handBuilt);
        Assert.Equal(expected.GetRawText(), element.GetRawText());
        Assert.Equal(Assert.IsType<JsonElement>(fromFactory).GetRawText(), element.GetRawText());
    }

    /// <summary>
    ///     Proves a structured result is serialized with the serializer options of the function
    ///     that returned it, not with options of the composition's choosing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_StructuredResult_IsSerializedWithTheFunctionsOwnOptions()
    {
        // Arrange: a hand-built function carrying snake_case serializer options
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        var value = new Sample("a", 3);
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_snake", value, options)],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_snake");

        // Assert: the JSON its own options produce
        var expected = JsonSerializer.SerializeToElement(value, options.GetTypeInfo(typeof(Sample)));
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(expected.GetRawText(), element.GetRawText());
        Assert.Contains("\"item_name\"", element.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves scalar results a hand-built function returns - a number, a Boolean, a decimal, an
    ///     enumeration value and a date - are each delivered as their JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ScalarResultsFromHandBuiltFunction_AreDeliveredAsTheirJson()
    {
        // Arrange: one scalar of each kind
        object[] values =
        [
            42,
            true,
            1.10m,
            DayOfWeek.Monday,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ];

        foreach (var value in values)
        {
            var tools = ToolOutputSpill.Apply(
                [new FixedFunction("probe_scalar", value)],
                new ToolLimits(maxResultCharacters: Ceiling));

            // Act: invoke it
            var result = await InvokeAsync(tools, "probe_scalar");

            // Assert: the JSON element the default options produce for the value
            var expected = JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType()));
            Assert.Equal(expected.GetRawText(), Assert.IsType<JsonElement>(result).GetRawText());
        }
    }

    /// <summary>
    ///     Proves a byte array is not binary content: it is measured, delivered and retained as the
    ///     base64 JSON string every adapter sends.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ByteArrayFromHandBuiltFunction_IsMeasuredAndRetainedAsItsBase64Json()
    {
        // Arrange: three bytes, and 1,500 bytes whose base64 JSON text is 2,002 characters
        var tools = ToolOutputSpill.Apply(
            [
                new FixedFunction("probe_small_bytes", new byte[] { 1, 2, 3 }),
                new FixedFunction("probe_bytes", Enumerable.Range(0, 1_500).Select(n => (byte)n).ToArray()),
            ],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke both
        var small = await InvokeAsync(tools, "probe_small_bytes");
        var large = await InvokeAsync(tools, "probe_bytes");

        // Assert: the small array as its base64 JSON string, the large one retained at its JSON size
        var element = Assert.IsType<JsonElement>(small);
        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), element.GetString());
        Assert.StartsWith("[Output output-1 from probe_bytes: 2002 characters", Assert.IsType<string>(large), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a result the serializer cannot handle fails the invocation with the serializer's
    ///     exception, exactly as the guarded factory fails for the same value.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_UnserializableResult_FailsTheInvocationAsTheGuardedFactoryDoes()
    {
        // Arrange: a self-referencing object graph and a delegate, each from a composed hand-built
        // function and from a factory-built tool
        var loop = new Loop();
        loop.Next = loop;
        var callback = (Func<int>)(() => 1);
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_loop", loop), new FixedFunction("probe_delegate", callback)],
            new ToolLimits(maxResultCharacters: Ceiling));
        var factoryLoop = GuardedToolFactory.Create((Func<object>)(() => loop), "probe_loop", "Returns a cycle.");
        var factoryDelegate = GuardedToolFactory.Create((Func<object>)(() => callback), "probe_delegate", "Returns a delegate.");

        // Act & Assert: the cycle throws a JSON exception and the delegate a not-supported
        // exception, from both paths alike
        await Assert.ThrowsAnyAsync<JsonException>(() => InvokeAsync(tools, "probe_loop"));
        await Assert.ThrowsAnyAsync<JsonException>(
            async () => await factoryLoop.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<NotSupportedException>(() => InvokeAsync(tools, "probe_delegate"));
        await Assert.ThrowsAnyAsync<NotSupportedException>(
            async () => await factoryDelegate.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves content other than text from a hand-built function is never serialized or
    ///     retained: it reaches the runtime as the very instance returned.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_NonTextContentFromHandBuiltFunction_PassesThroughUnchanged()
    {
        // Arrange: function-call and usage content from hand-built functions
        var call = new FunctionCallContent("c1", "other");
        var usage = new UsageContent(new UsageDetails { InputTokenCount = 3 });
        var tools = ToolOutputSpill.Apply(
            [new FixedFunction("probe_call", call), new FixedFunction("probe_usage", usage)],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke both
        var callResult = await InvokeAsync(tools, "probe_call");
        var usageResult = await InvokeAsync(tools, "probe_usage");

        // Assert: the very instances returned
        Assert.Same(call, callResult);
        Assert.Same(usage, usageResult);
    }

    /// <summary>
    ///     Proves the preview is clamped so it and its header fit the smallest ceiling retention
    ///     accepts, and that the header alone is delivered when the preview size admits no line.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_PreviewIsClampedToTheCeilingAndZeroPreviewDeliversTheHeader()
    {
        // Arrange: one composition at the minimum ceiling, one whose preview size admits no line
        var clampedTools = ToolOutputSpill.Apply(
            [TextTool("probe_big", Lines(500))],
            new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters));
        var headerTools = ToolOutputSpill.Apply(
            [TextTool("probe_big", Lines(500))],
            new ToolLimits(maxResultCharacters: Ceiling),
            new ToolOutputOptions(previewCharacters: 0));

        // Act: invoke both
        var clamped = Assert.IsType<string>(await InvokeAsync(clampedTools, "probe_big"));
        var headerOnly = Assert.IsType<string>(await InvokeAsync(headerTools, "probe_big"));

        // Assert: both fit their ceiling; the first shows lines, the second is the reference alone
        Assert.True(clamped.Length <= ToolOutputSpill.MinimumResultCharacters, $"The preview is {clamped.Length} characters.");
        Assert.Contains("\n  1| line 1", clamped, StringComparison.Ordinal);
        Assert.True(headerOnly.Length <= Ceiling, $"The header is {headerOnly.Length} characters.");
        Assert.Contains("No lines fit in the preview.", headerOnly, StringComparison.Ordinal);
        Assert.EndsWith("]", headerOnly, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a preview holding only tail lines - because the first line is too long for the
    ///     head's share - names the lines it shows rather than claiming none fit.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_TailOnlyPreview_HeaderNamesTheTailLines()
    {
        // Arrange: a first line too long for the head's three quarters, then eight short lines
        var output = new string('x', 700) + "\n" + Lines(8);
        var tools = ToolOutputSpill.Apply(
            [MarkerTool("probe_tail", output, "The output exceeds the limit.")],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_tail"));

        // Assert: the header names the tail it shows, within the ceiling
        Assert.Contains("Showing lines 2-9.", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("No lines fit", preview, StringComparison.Ordinal);
        Assert.Contains("[... lines 1-1 omitted ...]", preview, StringComparison.Ordinal);
        Assert.EndsWith("9| line 8", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= Ceiling, $"The preview is {preview.Length} characters.");
    }

    /// <summary>
    ///     Proves retention rejects a result ceiling below the published minimum, which no preview
    ///     naming its reference could fit within, and accepts the minimum itself.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_ResultCeilingBelowMinimum_ThrowsArgumentException()
    {
        // Arrange: a tool, and ceilings at zero, far below, and one below the minimum
        var tool = TextTool("probe_one", "a");

        // Act & Assert: each is rejected naming the limits, and the minimum is accepted
        foreach (var ceiling in new[] { 0, 10, ToolOutputSpill.MinimumResultCharacters - 1 })
        {
            var error = Assert.Throws<ArgumentException>(
                () => ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: ceiling)));
            Assert.Equal("limits", error.ParamName);
            Assert.Contains("at least 1000 characters", error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(
            3,
            ToolOutputSpill.Apply([tool], new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters)).Count);
    }

    /// <summary>
    ///     Proves retention rejects a hand-built tool whose name is longer than any provider accepts,
    ///     because the name is part of every preview header, and accepts a name at the limit.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_ToolNameLongerThanTheLimit_ThrowsArgumentException()
    {
        // Arrange: hand-built tools one character over and exactly at the limit
        var overlong = AIFunctionFactory.Create(() => "a", "probe_" + new string('a', ToolName.MaxLength - 5));
        var atLimit = AIFunctionFactory.Create(() => "a", "probe_" + new string('a', ToolName.MaxLength - 6));

        // Act & Assert: the over-long name is rejected; the name at the limit is composed
        var error = Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([overlong], ToolLimits.Default));
        Assert.Equal("tools", error.ParamName);
        Assert.Contains("longer than 64 characters", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, ToolOutputSpill.Apply([atLimit], ToolLimits.Default).Count);
    }

    /// <summary>
    ///     Proves the published minimum covers the longest fixed part a preview can have: the header
    ///     at its widest, the omission marker and its line break.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_MinimumResultCharacters_CoversTheWorstCaseHeader()
    {
        // Act: compose the worst case from the code that composes every preview
        var overhead = ToolOutputSpill.WorstCaseOverheadCharacters();

        // Assert: the minimum leaves room for it
        Assert.True(
            overhead <= ToolOutputSpill.MinimumResultCharacters,
            $"The worst-case overhead is {overhead} characters.");
    }

    /// <summary>
    ///     Proves every preview fits the minimum ceiling across the inputs that lengthen its header:
    ///     a name at the length limit, wrapped lines, multi-digit references, few and many lines,
    ///     and every preview size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_MinimumCeiling_EveryPreviewFitsAcrossWorstCaseHeaders()
    {
        // Arrange: outputs that stress the header and body, each from a tool named at the limit
        string[] outputs =
        [
            Lines(500),
            new string('x', 10_000),
            new string('x', 3_000) + "\n" + new string('y', 3_000) + "\nend",
            new string('x', 900) + "\n" + Lines(8),
            string.Join("\n", Enumerable.Range(1, 9).Select(n => new string((char)('a' + n), 300))),
            new string('z', 2_500) + "\n" + Lines(400),
            "a\nb",
        ];
        var limits = new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters);
        var names = outputs.Select((_, index) => "probe_" + (char)('a' + index) + new string('q', ToolName.MaxLength - 7)).ToList();
        ToolOutputOptions[] options =
        [
            ToolOutputOptions.Default,
            new(previewCharacters: 0),
            new(previewCharacters: 100),
            new(previewCharacters: 100_000),
        ];

        foreach (var option in options)
        {
            var tools = ToolOutputSpill.Apply(
                outputs.Select((output, index) => MarkerTool(names[index], output, "Too large.")),
                limits,
                option);

            // Act: invoke every tool repeatedly, so references reach two digits
            for (var round = 0; round < 2; round++)
            {
                for (var index = 0; index < outputs.Length; index++)
                {
                    var preview = Assert.IsType<string>(await InvokeAsync(tools, names[index]));

                    // Assert: a reference within the ceiling, whatever the header carries
                    Assert.StartsWith("[Output output-", preview, StringComparison.Ordinal);
                    Assert.True(
                        preview.Length <= ToolOutputSpill.MinimumResultCharacters,
                        $"The preview of output {index} is {preview.Length} characters.");
                }
            }
        }
    }

    /// <summary>
    ///     Proves the header says so when long lines were wrapped for paging.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_WrappedOutput_HeaderNotesTheWrapping()
    {
        // Arrange: one very long line
        var tools = ToolOutputSpill.Apply(
            [TextTool("probe_long", new string('x', 10_000))],
            new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "probe_long"));

        // Assert: the header names the wrapping
        Assert.Contains("were wrapped onto continuation lines", preview, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the readers are appended after the supplied tools, in a fixed order, and the
    ///     supplied tools keep their metadata.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_Tools_AreFollowedByTheReaders()
    {
        // Arrange: two tools
        var first = TextTool("probe_first", "a");
        var second = TextTool("probe_second", "b");

        // Act: compose retention over them
        var tools = ToolOutputSpill.Apply([first, second], ToolLimits.Default);

        // Assert: the supplied tools in order, then the two readers, with metadata unchanged
        Assert.Equal(
            ["probe_first", "probe_second", ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName],
            tools.Select(tool => tool.Name));
        Assert.Equal(first.Description, tools[0].Description);
        Assert.Equal(first.JsonSchema.GetRawText(), tools[0].JsonSchema.GetRawText());
    }

    /// <summary>
    ///     Proves a reader's own result is never retained: a page over the ceiling is refused, not
    ///     turned into another reference.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ReaderResults_AreNeverRetained()
    {
        // Arrange: a retained output
        var tools = ToolOutputSpill.Apply([TextTool("probe_big", Lines(500))], new ToolLimits(maxResultCharacters: Ceiling));
        await InvokeAsync(tools, "probe_big");

        // Act: ask for a page far larger than the ceiling
        var page = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 500,
        }));

        // Assert: refused with the ceiling, and no second reference was issued
        Assert.StartsWith("Denied (ResourceTooLarge)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("output-2", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a tool not built by the guarded factory is wrapped too, so a hand-built tool's
    ///     oversized output is retained.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_ToolNotBuiltByTheFactory_IsRetainedToo()
    {
        // Arrange: a function built directly by the underlying factory
        var big = Lines(500);
        var raw = AIFunctionFactory.Create(() => big, "probe_raw");
        var tools = ToolOutputSpill.Apply([raw], new ToolLimits(maxResultCharacters: Ceiling));

        // Act: invoke it
        var result = await InvokeAsync(tools, "probe_raw");

        // Assert: a reference, however the factory shaped the text
        Assert.StartsWith("[Output output-1 from probe_raw", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each application allocates its own store, so one composition cannot read another's
    ///     outputs, and the tools supplied are not altered.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolOutputSpill_Apply_TwoApplications_DoNotShareReferencesOrAlterTheSuppliedTools()
    {
        // Arrange: one declared-oversized tool composed twice
        const string message = "The listing exceeds the limit.";
        var tool = MarkerTool("probe_marker", Lines(10), message);
        var first = ToolOutputSpill.Apply([tool], ToolLimits.Default);
        var second = ToolOutputSpill.Apply([tool], ToolLimits.Default);

        // Act: retain an output through the first, read it through the second, and invoke the
        // original tool directly
        await InvokeAsync(first, "probe_marker");
        var fromSecond = Assert.IsType<string>(await InvokeAsync(second, ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }));
        var original = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: the second store never issued the reference, and the original still refuses
        Assert.Contains("never retained", fromSecond, StringComparison.Ordinal);
        Assert.Equal(ToolResult.Denied(DenialReason.ResourceTooLarge, message), original);
    }

    /// <summary>
    ///     Proves a list retention cannot be composed over is rejected.
    /// </summary>
    [Fact]
    public void ToolOutputSpill_Apply_InvalidLists_Throw()
    {
        // Arrange: tools for the invalid cases
        var tool = TextTool("probe_one", "a");
        var reserved = TextTool("tool_output_extra", "a");

        // Act & Assert: absent arguments, a null entry, a duplicate name and a reserved name
        Assert.Throws<ArgumentNullException>(() => ToolOutputSpill.Apply(null!, ToolLimits.Default));
        Assert.Throws<ArgumentNullException>(() => ToolOutputSpill.Apply([tool], null!));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([tool, null!], ToolLimits.Default));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([tool, tool], ToolLimits.Default));
        Assert.Throws<ArgumentException>(() => ToolOutputSpill.Apply([reserved], ToolLimits.Default));
    }

    /// <summary>
    ///     Builds a guarded tool returning fixed text.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="text">The text the tool returns.</param>
    /// <returns>The tool.</returns>
    internal static AIFunction TextTool(string name, string text) => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(text))),
        name,
        "Returns fixed text.");

    /// <summary>
    ///     Builds a guarded tool declaring its output oversized.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="text">The full output.</param>
    /// <param name="message">The refusal message given without retention.</param>
    /// <returns>The tool.</returns>
    internal static AIFunction MarkerTool(string name, string text, string message) => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult(ToolResult.Oversized(text, message))),
        name,
        "Returns an oversized output.");

    /// <summary>
    ///     Builds a guarded tool returning content built on each call.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="content">Builds the content the tool returns.</param>
    /// <returns>The tool.</returns>
    private static AIFunction ContentTool(string name, Func<IEnumerable<AIContent>> content) => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult<object>(content())),
        name,
        "Returns content.");

    /// <summary>
    ///     Builds numbered text of the requested number of lines.
    /// </summary>
    /// <param name="count">The number of lines.</param>
    /// <returns>Lines of the form <c>line N</c>, joined by line feeds.</returns>
    internal static string Lines(int count) =>
        string.Join("\n", Enumerable.Range(1, count).Select(n => "line " + n));

    /// <summary>
    ///     Invokes the named tool from a composed list.
    /// </summary>
    /// <param name="tools">The composed list.</param>
    /// <param name="name">The tool to invoke.</param>
    /// <param name="arguments">The arguments, or null for none.</param>
    /// <returns>The tool's result.</returns>
    internal static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments? arguments = null)
    {
        var tool = tools.Single(candidate => candidate.Name == name);
        return await tool.InvokeAsync(arguments ?? new AIFunctionArguments(), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     A hand-built function returning one fixed value, as a function not built by the guarded
    ///     factory may return a shape the factory never produces.
    /// </summary>
    private sealed class FixedFunction : AIFunction
    {
        /// <summary>
        ///     The function's name.
        /// </summary>
        private readonly string _name;

        /// <summary>
        ///     The value every invocation returns.
        /// </summary>
        private readonly object? _result;

        /// <summary>
        ///     The function's own serializer options, or null for the default.
        /// </summary>
        private readonly JsonSerializerOptions? _serializerOptions;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedFunction"/> class.
        /// </summary>
        /// <param name="name">The function's name.</param>
        /// <param name="result">The value every invocation returns.</param>
        /// <param name="serializerOptions">The function's own serializer options, or null for the default.</param>
        public FixedFunction(string name, object? result, JsonSerializerOptions? serializerOptions = null)
        {
            _name = name;
            _result = result;
            _serializerOptions = serializerOptions;
        }

        /// <inheritdoc/>
        public override string Name => _name;

        /// <inheritdoc/>
        public override JsonSerializerOptions JsonSerializerOptions => _serializerOptions ?? base.JsonSerializerOptions;

        /// <inheritdoc/>
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken) => new(_result);
    }

    /// <summary>
    ///     A small record a hand-built function may return as it is.
    /// </summary>
    /// <param name="ItemName">A name, whose casing the serializer options decide.</param>
    /// <param name="Count">A count.</param>
    private sealed record Sample(string ItemName, int Count);

    /// <summary>
    ///     An object that can refer to itself, forming a graph the serializer cannot handle.
    /// </summary>
    private sealed class Loop
    {
        /// <summary>
        ///     Gets or sets the next object in the graph.
        /// </summary>
        public Loop? Next { get; set; }
    }

    /// <summary>
    ///     A sequence of content that can be enumerated only once, as a lazily produced sequence
    ///     may be; a second enumeration throws.
    /// </summary>
    private sealed class SinglePassSequence : IEnumerable<AIContent>
    {
        /// <summary>
        ///     The parts the one enumeration yields.
        /// </summary>
        private readonly AIContent[] _parts;

        /// <summary>
        ///     Whether the sequence has been enumerated.
        /// </summary>
        private bool _enumerated;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SinglePassSequence"/> class.
        /// </summary>
        /// <param name="parts">The parts the one enumeration yields.</param>
        public SinglePassSequence(params AIContent[] parts)
        {
            _parts = parts;
        }

        /// <inheritdoc/>
        public IEnumerator<AIContent> GetEnumerator()
        {
            if (_enumerated)
            {
                throw new InvalidOperationException("The sequence was already enumerated.");
            }

            _enumerated = true;
            return ((IEnumerable<AIContent>)_parts).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
