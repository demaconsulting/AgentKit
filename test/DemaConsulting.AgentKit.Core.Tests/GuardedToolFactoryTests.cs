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
            "image_probe",
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
