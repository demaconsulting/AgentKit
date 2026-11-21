using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="GuardedToolFactory"/> class.
/// </summary>
/// <remarks>
///     <para>
///     <b>The requirement-linked scenarios declare their tool delegate as
///     <c>Task&lt;object&gt;</c> deliberately, and must stay that way.</b> The underlying
///     function factory decides whether to serialize a result by the delegate's <em>declared</em>
///     return type, not by the runtime type of the value returned. A delegate declared
///     <c>Task&lt;DataContent&gt;</c> is passed through unchanged even with no guard at all, so
///     a test written that way would pass with the guard deleted and would prove nothing.
///     </para>
///     <para>
///     The explicit <c>(Func&lt;Task&lt;object&gt;&gt;)</c> casts are therefore load-bearing
///     rather than ceremony: removing them is a regression in the evidence, not a
///     simplification.
///     </para>
/// </remarks>
public class GuardedToolFactoryTests
{
    /// <summary>
    ///     The bytes every scenario uses as a stand-in for real content.
    /// </summary>
    private static readonly byte[] SampleBytes = [1, 2, 3];

    /// <summary>
    ///     Proves that binary content returned by an object-declared tool is not serialized
    ///     into JSON.
    /// </summary>
    /// <remarks>
    ///     The delegate is declared <c>Task&lt;object&gt;</c> on purpose — that is the shape the
    ///     factory would otherwise serialize, and the only shape in which this scenario means
    ///     anything. The negative assertion names the failure being prevented: without the
    ///     guard the runtime receives a <see cref="JsonElement"/> holding a <c>data:</c> URI,
    ///     the provider never recognizes an attachment, and the model fabricates a description
    ///     of content it never saw.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_BinaryResultIsNotJsonSerialized()
    {
        // Arrange: a tool whose declared return type is object, returning binary content
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Binary(SampleBytes, "image/png"))),
            "binary_probe",
            "Returns binary content.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the content arrives intact, and specifically not as serialized JSON
        Assert.IsType<DataContent>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves that a captioned image returned by an object-declared tool survives as a
    ///     content list.
    /// </summary>
    /// <remarks>
    ///     Declared <c>Task&lt;object&gt;</c> for the reason given on the type: a strongly-typed
    ///     declaration would pass without the guard. Without the guard the two parts are
    ///     flattened into a single JSON array and neither part remains recognizable to a
    ///     provider.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_CaptionedImageSurvivesAsContentList()
    {
        // Arrange: a tool whose declared return type is object, returning a captioned image
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(
                ToolResult.Image(SampleBytes, "image/png", "A screenshot."))),
            "image_read",
            "Returns a captioned image.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the exact caption-then-image shape the result constructor produced
        var parts = Assert.IsAssignableFrom<IList<AIContent>>(result);
        Assert.Equal(2, parts.Count);
        Assert.IsType<TextContent>(parts[0]);
        Assert.IsType<DataContent>(parts[1]);
    }

    /// <summary>
    ///     Proves that a refusal returned by an object-declared tool survives as a plain string.
    /// </summary>
    /// <remarks>
    ///     This pins the benign but observable consequence of the guard on the refusal path:
    ///     the refusal reaches the runtime as a raw <see cref="string"/> rather than as a
    ///     <see cref="JsonElement"/> wrapping a JSON string. Both are consumable, so nothing is
    ///     broken — but a reader who expected the wrapped form must not mistake this for a
    ///     defect. Declared <c>Task&lt;object&gt;</c> for the reason given on the type.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_DenialSurvivesAsString()
    {
        // Arrange: a tool whose declared return type is object, refusing the request
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(
                ToolResult.Denied(DenialReason.PathNotPermitted, "the location is not permitted"))),
            "denial_probe",
            "Always refuses.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the raw refusal text, not a JSON wrapping of it
        var text = Assert.IsType<string>(result);
        Assert.Contains("PathNotPermitted", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the guard is applied to a synchronous tool as well as an asynchronous
    ///     one.
    /// </summary>
    /// <remarks>
    ///     The declared return type is <see cref="object"/> — the synchronous counterpart of
    ///     the trapped case — so this scenario likewise fails if the guard is removed.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_SynchronousDelegate_ResultIsStillPassedThrough()
    {
        // Arrange: a synchronous tool whose declared return type is object
        var function = GuardedToolFactory.Create(
            (Func<object>)(() => ToolResult.Binary(SampleBytes, "image/png")),
            "sync_probe",
            "Returns binary content synchronously.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the guard does not depend on the tool being asynchronous
        Assert.IsType<DataContent>(result);
    }

    /// <summary>
    ///     Proves that the guard is applied to a tool that takes parameters, and that the
    ///     parameters still bind.
    /// </summary>
    /// <remarks>
    ///     Declared <c>Task&lt;object&gt;</c> for the reason given on the type. The parameter
    ///     matters independently: the guard governs result delivery only, and must leave the
    ///     factory's own parameter binding and schema generation untouched.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_ParameterizedDelegate_ResultIsStillPassedThrough()
    {
        // Arrange: a tool taking one parameter, whose declared return type is object
        var function = GuardedToolFactory.Create(
            (Func<string, Task<object>>)(path => Task.FromResult(
                ToolResult.Text("content of " + path))),
            "text_file_read",
            "Reads a text file.");

        // Act: invoke the tool with an argument, as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["path"] = "a.txt" },
            TestContext.Current.CancellationToken);

        // Assert: the argument bound and the result was delivered unchanged
        Assert.Equal("content of a.txt", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that an anonymous object returned by an object-declared tool is serialized to
    ///     JSON rather than handed to the provider as a raw CLR instance.
    /// </summary>
    /// <remarks>
    ///     This is the scenario blanket passthrough failed. Preserving every result
    ///     indiscriminately delivered the anonymous object itself, which the provider reported
    ///     back as a compiler-generated type name and could not read. Selecting by shape keeps
    ///     content intact and serializes everything else exactly as the factory would.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_AnonymousObjectIsSerializedToJsonElement()
    {
        // Arrange: a tool whose declared return type is object, returning an anonymous object
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult<object>(new { name = "a.txt", size = 3 })),
            "structured_probe",
            "Returns an anonymous object.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: readable JSON rather than an opaque instance the provider cannot interpret
        var element = Assert.IsType<JsonElement>(result);
        Assert.Contains("a.txt", element.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a structured result is serialized to JSON on its way to the runtime.
    /// </summary>
    /// <remarks>
    ///     Declared <c>Task&lt;object&gt;</c> for the reason given on the type. The structured
    ///     result constructor is the uniform way a tool expresses data that is neither text nor
    ///     content, and JSON is the form in which a provider can read it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_StructuredResultIsSerializedToJsonElement()
    {
        // Arrange: a tool whose declared return type is object, returning structured data
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(
                ToolResult.Structured(new Dictionary<string, int> { ["matches"] = 2 }))),
            "counting_probe",
            "Returns structured data.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the data reaches the model as JSON it can read
        var element = Assert.IsType<JsonElement>(result);
        Assert.Contains("matches", element.GetRawText(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a null result is delivered unchanged rather than serialized.
    /// </summary>
    /// <remarks>
    ///     There is nothing to serialize and nothing for a provider to misread, so the absence
    ///     is carried through as an absence. Declared <c>Task&lt;object?&gt;</c>, the nullable
    ///     counterpart of the trapped case.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_DelegateDeclaredReturningObject_NullResultIsPassedThrough()
    {
        // Arrange: a tool whose declared return type is object, returning nothing
        var function = GuardedToolFactory.Create(
            (Func<Task<object?>>)(() => Task.FromResult<object?>(null)),
            "empty_probe",
            "Returns nothing.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: an absence, not a JSON null wrapped in an element
        Assert.Null(result);
    }

    /// <summary>
    ///     Proves that a name carrying no family prefix is rejected at construction.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_Create_UnprefixedName_ThrowsArgumentException()
    {
        // Act & Assert: the factory applies the naming convention before it builds anything
        Assert.Throws<ArgumentException>(
            () => GuardedToolFactory.Create(
                (Func<object>)(() => ToolResult.Text("ok")),
                "readfile",
                "Reads a file."));
    }

    /// <summary>
    ///     Proves that a name reserved by the Agent Framework is rejected at construction.
    /// </summary>
    /// <remarks>
    ///     Rejecting it at the factory is what makes the collision impossible in practice: a
    ///     tool built any other way is not a supported tool.
    /// </remarks>
    [Fact]
    public void GuardedToolFactory_Create_ReservedName_ThrowsArgumentException()
    {
        // Act & Assert: the bare file access names cannot be claimed through the factory
        Assert.Throws<ArgumentException>(
            () => GuardedToolFactory.Create(
                (Func<object>)(() => ToolResult.Text("ok")),
                "read",
                "Reads a file."));
    }

    /// <summary>
    ///     Proves that the validated name is the name the created tool carries.
    /// </summary>
    /// <remarks>
    ///     With no name supplied the underlying factory derives a compiler-generated identifier
    ///     from the delegate, which is meaningless to a model and unstable across
    ///     recompilation. The factory therefore always sets the name.
    /// </remarks>
    [Fact]
    public void GuardedToolFactory_Create_ValidName_IsCarriedByTheCreatedTool()
    {
        // Arrange & Act: create a tool from a lambda, which has no usable name of its own
        var function = GuardedToolFactory.Create(
            (Func<object>)(() => ToolResult.Text("ok")),
            "text_file_read",
            "Reads a text file.");

        // Assert: the supplied name reaches the model, not a compiler-generated identifier
        Assert.Equal("text_file_read", function.Name);
    }

    /// <summary>
    ///     Proves that a tool cannot be created without a description.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_Create_EmptyDescription_ThrowsArgumentException()
    {
        // Act & Assert: a tool with no description leaves the model guessing what it does
        Assert.Throws<ArgumentException>(
            () => GuardedToolFactory.Create(
                (Func<object>)(() => ToolResult.Text("ok")),
                "text_file_read",
                string.Empty));
    }

    /// <summary>
    ///     Proves that the supplied description is the description the created tool carries.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_Create_Description_IsCarriedByTheCreatedTool()
    {
        // Arrange & Act: create a tool with a description a model could act on
        const string description = "Reads a text file and returns its contents.";
        var function = GuardedToolFactory.Create(
            (Func<object>)(() => ToolResult.Text("ok")),
            "text_file_read",
            description);

        // Assert: the description reaches the model rather than defaulting to empty
        Assert.Equal(description, function.Description);
    }

    /// <summary>
    ///     Proves that an oversized-output marker never reaches the runtime from a tool composed
    ///     without retention: it becomes exactly the refusal the tool always gave.
    /// </summary>
    /// <remarks>
    ///     This is what makes retention opt-in without changing any existing refusal: a tool that
    ///     now returns <see cref="ToolResult.Oversized"/> instead of a refusal is observed by a host
    ///     that did not opt in to give the identical text.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_OversizedMarkerWithoutRetention_ReturnsTheIdenticalRefusal()
    {
        // Arrange: a tool declaring its output oversized
        const string message = "The listing exceeds the 32000-character result limit.";
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Oversized(new string('x', 40_000), message))),
            "oversized_probe",
            "Returns an oversized output.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: byte-for-byte the refusal composed directly
        Assert.Equal(
            Assert.IsType<string>(ToolResult.Denied(DenialReason.ResourceTooLarge, message)),
            Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that a tool composed without retention still delivers an oversized ordinary
    ///     result unchanged, exactly as before retention existed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_Create_OversizedTextWithoutRetention_IsPassedThroughUnchanged()
    {
        // Arrange: a tool returning text well above the default result ceiling
        var text = new string('x', ToolLimits.DefaultMaxResultCharacters * 2);
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(text))),
            "large_probe",
            "Returns a large text.");

        // Act: invoke the tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the same text, neither refused nor retained
        Assert.Same(text, result);
    }

    /// <summary>
    ///     Proves that the constructed tool carries exactly the metadata the underlying factory
    ///     would have given it, so every consumer that reads a tool's description, schema or
    ///     method sees the same thing it saw before the tool was wrapped.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_Create_ConstructedTool_PreservesEveryPieceOfMetadata()
    {
        // Arrange: the same delegate built guarded and built directly by the underlying factory
        var method = (Func<string, int, Task<object>>)((path, count) =>
            Task.FromResult(ToolResult.Text(path + count)));
        const string description = "Reads a number of lines from a file.";
        var guarded = GuardedToolFactory.Create(method, "text_file_read", description);
        var direct = AIFunctionFactory.Create(
            method,
            new AIFunctionFactoryOptions { Name = "text_file_read", Description = description });

        // Act / Assert: name, description, schemas, options, method and properties all match, and
        // a service lookup for the function type answers with the tool itself
        Assert.Equal(direct.Name, guarded.Name);
        Assert.Equal(direct.Description, guarded.Description);
        Assert.Equal(direct.JsonSchema.GetRawText(), guarded.JsonSchema.GetRawText());
        Assert.Equal(direct.ReturnJsonSchema?.GetRawText(), guarded.ReturnJsonSchema?.GetRawText());
        Assert.Same(direct.JsonSerializerOptions, guarded.JsonSerializerOptions);
        Assert.Equal(direct.UnderlyingMethod, guarded.UnderlyingMethod);
        Assert.Equal(direct.AdditionalProperties, guarded.AdditionalProperties);
        Assert.Same(guarded, guarded.GetService(typeof(AIFunction)));
        Assert.Equal(direct.ToString(), guarded.ToString());
    }

    /// <summary>
    ///     Proves that a tool cannot be created without an implementation.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_Create_NullDelegate_ThrowsArgumentNullException()
    {
        // Act & Assert: there is nothing to guard, so this is a programming error
        Assert.Throws<ArgumentNullException>(
            () => GuardedToolFactory.Create(null!, "text_file_read", "Reads a text file."));
    }

    /// <summary>
    ///     Proves that a function built with the plain factory and bounded by <c>AsGuarded</c> has
    ///     an oversized text result refused, naming the size and the ceiling.
    /// </summary>
    /// <remarks>
    ///     This is the scenario the extension exists for: a 300-row report built by
    ///     <see cref="AIFunctionFactory"/> returned about 6,350 characters against a 1,500-character
    ///     limit, and nothing enforced the limit.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_OversizedTextResult_IsRefusedWithSizeAndLimit()
    {
        // Arrange: a foreign tool whose text result is far beyond a small ceiling
        var report = AIFunctionFactory.Create(
            () => string.Join("\n", Enumerable.Range(1, 300).Select(n => "row " + n + " value " + (n * n))),
            "report_rows",
            "Returns the report rows.");
        var guarded = report.AsGuarded(new ToolLimits(maxResultCharacters: 1_500));

        // Act: invoke it as the runtime would
        var result = await guarded.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: a ResourceTooLarge refusal naming the tool, the size and the ceiling
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("'report_rows'", text, StringComparison.Ordinal);
        Assert.Contains("1500-character result limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a structured result is serialized with the function's own options and then
    ///     measured as that JSON, so an oversized object is refused rather than passed through.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_OversizedStructuredResult_IsSerializedThenRefused()
    {
        // Arrange: a foreign tool returning a collection of anonymous objects
        var report = AIFunctionFactory.Create(
            () => (object)Enumerable.Range(1, 300).Select(n => new { row = n, square = n * n }).ToList(),
            "report_json",
            "Returns the report as JSON.");
        var guarded = report.AsGuarded(new ToolLimits(maxResultCharacters: 1_500));

        // Act: invoke it as the runtime would
        var result = await guarded.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: the serialized size exceeded the ceiling and was refused
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that results within the ceilings are delivered unchanged in every shape: text,
    ///     structured data as its JSON document, text content, and an image within the byte limit.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_ResultWithinLimits_IsUnchanged()
    {
        // Arrange: four small foreign tools and generous ceilings
        var limits = new ToolLimits(maxResultCharacters: 1_500, maxBinaryBytes: 1_024);
        var image = new DataContent(SampleBytes, "image/png");
        var text = AIFunctionFactory.Create(() => "short", "probe_text", "Text.").AsGuarded(limits);
        var structured = AIFunctionFactory.Create(() => (object)new { a = 1 }, "probe_json", "Json.").AsGuarded(limits);
        var content = AIFunctionFactory.Create(() => new TextContent("hello"), "probe_content", "Content.").AsGuarded(limits);
        var binary = AIFunctionFactory.Create(() => image, "probe_image", "Image.").AsGuarded(limits);

        // Act: invoke each
        var token = TestContext.Current.CancellationToken;
        var textResult = await text.InvokeAsync(new AIFunctionArguments(), token);
        var structuredResult = await structured.InvokeAsync(new AIFunctionArguments(), token);
        var contentResult = await content.InvokeAsync(new AIFunctionArguments(), token);
        var binaryResult = await binary.InvokeAsync(new AIFunctionArguments(), token);

        // Assert: each is delivered as the function produced it, structured data as JSON
        Assert.Equal("short", Assert.IsType<JsonElement>(textResult).GetString());
        Assert.Equal(1, Assert.IsType<JsonElement>(structuredResult).GetProperty("a").GetInt32());
        Assert.Equal("hello", Assert.IsType<TextContent>(contentResult).Text);
        Assert.Same(image, binaryResult);
    }

    /// <summary>
    ///     Proves that binary content beyond the byte ceiling is refused, naming the bytes and the
    ///     limit.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_OversizedBinaryResult_IsRefused()
    {
        // Arrange: a foreign tool returning a caption and an image larger than the byte ceiling
        var parts = new List<AIContent> { new TextContent("A chart."), new DataContent(new byte[2_048], "image/png") };
        var chart = AIFunctionFactory.Create(() => parts, "chart_render", "Renders a chart.")
            .AsGuarded(new ToolLimits(maxBinaryBytes: 1_024));

        // Act: invoke it as the runtime would
        var result = await chart.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: refused, naming the bytes carried and the limit
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("2048 bytes", text, StringComparison.Ordinal);
        Assert.Contains("1024-byte limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a single-pass sequence of content is enumerated exactly once and delivered as
    ///     the parts enumerated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_SinglePassSequence_IsEnumeratedOnce()
    {
        // Arrange: a foreign tool returning a sequence that counts its enumerations
        var enumerations = 0;
        IEnumerable<AIContent> Parts()
        {
            enumerations++;
            yield return new TextContent("one");
            yield return new TextContent("two");
        }

        var tool = new SequenceFunction(Parts).AsGuarded(ToolLimits.Default);

        // Act: invoke it as the runtime would
        var result = await tool.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: one enumeration, and both parts delivered in order
        Assert.Equal(1, enumerations);
        var delivered = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result);
        Assert.Equal(["one", "two"], delivered.Cast<TextContent>().Select(part => part.Text));
    }

    /// <summary>
    ///     Proves that the bounded function carries the name, description and schema of the
    ///     function it wraps.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_AsGuarded_PreservesNameDescriptionSchema()
    {
        // Arrange: a foreign tool with a parameter
        var inner = AIFunctionFactory.Create((int rows) => rows, "report_rows", "Returns rows.");

        // Act: bound it
        var guarded = inner.AsGuarded(ToolLimits.Default);

        // Assert: every piece of metadata a runtime reads is the inner function's
        Assert.Equal(inner.Name, guarded.Name);
        Assert.Equal(inner.Description, guarded.Description);
        Assert.Equal(inner.JsonSchema.GetRawText(), guarded.JsonSchema.GetRawText());
        Assert.Same(inner.JsonSerializerOptions, guarded.JsonSerializerOptions);
    }

    /// <summary>
    ///     Proves that a wrapped approval-required function is still discovered through service
    ///     lookup, which is how agent runtimes detect that a call needs approval.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_AsGuarded_ApprovalRequiredInner_DiscoverableThroughGetService()
    {
        // Arrange: a foreign tool that requires approval
        var approval = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create(() => "done", "files_purge", "Purges files."));

        // Act: bound it
        var guarded = approval.AsGuarded(ToolLimits.Default);

        // Assert: the approval requirement is still discoverable
        Assert.Same(approval, guarded.GetService(typeof(ApprovalRequiredAIFunction)));
    }

    /// <summary>
    ///     Proves that a bounded function later composed with retention retains an oversized text
    ///     result for paging instead of refusing it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_ThenRetentionWrap_RetainsInsteadOfRefusing()
    {
        // Arrange: a bounded foreign tool, composed with retention at the same ceiling
        var limits = new ToolLimits(maxResultCharacters: 1_500);
        var report = AIFunctionFactory.Create(
            () => string.Join("\n", Enumerable.Range(1, 300).Select(n => "row " + n)),
            "report_rows",
            "Returns the report rows.").AsGuarded(limits);
        var composed = ToolOutputSpill.Apply([report], limits);

        // Act: invoke the composed tool
        var result = await composed[0].InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: a preview naming the reference rather than a refusal
        Assert.StartsWith("[Output output-1 from report_rows", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that bounding an already bounded function replaces its ceilings rather than
    ///     stacking a second wrapper.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task GuardedToolFactory_AsGuarded_OnGuardedFunction_ReplacesLimitsWithoutDoubleWrapping()
    {
        // Arrange: a 2,000-character result bounded tightly, then re-bounded generously
        var inner = AIFunctionFactory.Create(() => new string('x', 2_000), "probe_text", "Text.");
        var tight = inner.AsGuarded(new ToolLimits(maxResultCharacters: 1_000));

        // Act: re-bound it and invoke
        var generous = tight.AsGuarded(new ToolLimits(maxResultCharacters: 5_000));
        var result = await generous.InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);

        // Assert: the new ceiling alone applies - a stacked wrapper would still hold the tight
        // ceiling beneath and refuse - so the 2,000-character result (2,002 as JSON) is delivered
        Assert.Equal(2_002, Assert.IsType<JsonElement>(result).GetRawText().Length);
    }

    /// <summary>
    ///     Proves that a missing function or missing ceilings is a programming error.
    /// </summary>
    [Fact]
    public void GuardedToolFactory_AsGuarded_NullFunctionOrLimits_Throws()
    {
        // Arrange: a valid function
        var inner = AIFunctionFactory.Create(() => "x", "probe_text", "Text.");

        // Act & Assert: each missing argument is refused
        Assert.Throws<ArgumentNullException>(() => ((AIFunction)null!).AsGuarded(ToolLimits.Default));
        Assert.Throws<ArgumentNullException>(() => inner.AsGuarded(null!));
    }

    /// <summary>
    ///     A function returning a sequence produced by a delegate, so a test controls how often the
    ///     sequence is enumerated.
    /// </summary>
    /// <param name="produce">Produces the sequence returned on each invocation.</param>
    private sealed class SequenceFunction(Func<IEnumerable<AIContent>> produce) : AIFunction
    {
        /// <summary>
        ///     Gets the tool's name.
        /// </summary>
        public override string Name => "probe_sequence";

        /// <summary>
        ///     Returns the produced sequence without enumerating it.
        /// </summary>
        /// <param name="arguments">The arguments, ignored.</param>
        /// <param name="cancellationToken">A token, ignored.</param>
        /// <returns>The sequence.</returns>
        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            return new ValueTask<object?>(produce());
        }
    }

    /// <summary>
    ///     Characterizes how the underlying function factory handles an object-declared return
    ///     when no result-delivery guard is supplied.
    /// </summary>
    /// <remarks>
    ///     This test characterizes third-party behavior in
    ///     <c>Microsoft.Extensions.AI.Abstractions</c> rather than behavior this library
    ///     promises, so it is linked to the off-the-shelf requirement describing what that
    ///     package supplies, not to a requirement describing what AgentKit does. The
    ///     distinction matters: if Microsoft later changes how an <c>object</c>-declared
    ///     return is handled, what has become untrue is a documented assumption about the
    ///     dependency, and failing the evidence for that assumption is the correct outcome.
    ///     The guard's own requirements are unaffected, because the guard keeps its promise
    ///     either way. Its name begins with the third-party type under characterization, not
    ///     with this library's unit, for the same reason; it lives beside the guard because it
    ///     is the evidence that justifies the guard's existence.
    /// </remarks>
    /// <returns>A task that completes when the behavior has been characterized.</returns>
    [Fact]
    public async Task AIFunctionFactory_UnguardedObjectReturn_FlattensDataContentToJsonElement()
    {
        // Arrange: the same tool built WITHOUT options and therefore without the guard.
        // NOTE: this is intentionally not requirement-linked - it records what the third-party
        // factory does today, so an upstream change fails here rather than in the guard's own
        // compliance evidence.
        var function = AIFunctionFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Binary(SampleBytes, "image/png"))),
            "unguarded_probe");

        // Act: invoke the unguarded tool as the runtime would
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the content was flattened into JSON, which is exactly the failure the guard
        // exists to prevent
        var element = Assert.IsType<JsonElement>(result);
        var text = element.GetRawText();
        Assert.Contains("$type", text, StringComparison.Ordinal);
        Assert.Contains("data", text, StringComparison.Ordinal);
    }
}
