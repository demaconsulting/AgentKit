using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The only supported way to construct a tool.
/// </summary>
/// <remarks>
///     <para>
///     <b>Result handling by the underlying factory depends on the tool delegate's
///     <em>declared</em> return type, not on the type of the value it actually returns.</b> A
///     delegate declared to return <see cref="object"/> or <c>Task&lt;object&gt;</c> has its
///     result serialized to JSON and handed to the runtime as a <c>JsonElement</c>: a returned
///     <see cref="DataContent"/> becomes a <c>data:</c> URI inside a JSON object, a returned
///     list of content becomes a JSON array, and a returned string becomes a JSON string. A
///     delegate declared to return <see cref="DataContent"/> — or any other concrete content
///     type — is passed through unchanged, with or without this guard.
///     </para>
///     <para>
///     A guarded tool is necessarily declared to return <see cref="object"/> or
///     <c>Task&lt;object&gt;</c>, because it returns a union: a refusal, or text, or content,
///     depending on what happened. No strongly-typed signature expresses that union and
///     survives — a bespoke result type is a plain object and is serialized just the same. The
///     natural, correct way to write a tool is therefore exactly the case the factory would
///     serialize, which is why the guard is applied unconditionally and why this is the only
///     supported construction path.
///     </para>
///     <para>
///     <b>The guard is selective, not a blanket passthrough.</b> Text, content and sequences of
///     content reach the runtime exactly as the tool produced them, because those are the
///     shapes a provider recognizes and the shapes serialization destroys. Anything else is
///     structured data and is serialized to a <c>JsonElement</c> exactly as the factory would
///     have done. Preserving everything indiscriminately was tried and is wrong: a tool
///     returning an anonymous object then reaches the provider as a raw CLR instance, which no
///     provider can read.
///     </para>
///     <para>
///     <b>Do not remove this guard after observing that a strongly-typed return works.</b> That
///     observation is true and irrelevant: it demonstrates the passthrough case, not the case
///     this library is in. The same caution applies to the tests. A test whose delegate is
///     declared <c>Task&lt;DataContent&gt;</c> passes with the guard deleted and proves
///     nothing; the requirement-linked tests declare <c>Task&lt;object&gt;</c> deliberately and
///     must stay that way.
///     </para>
///     <para>
///     The failure mode without the guard is severe and silent. The provider never receives an
///     image attachment, and the model — having been told a tool returned an image — reports
///     that it can see the image and then fabricates a description of it. There is no error to
///     notice.
///     </para>
///     <para>
///     The guard also changes the <b>refusal</b> path in a way worth stating so it is not
///     mistaken for a defect: a refusal string reaches the runtime as a raw
///     <see cref="string"/> rather than as a <c>JsonElement</c> wrapping a JSON string. Both
///     are consumable, so the change is benign — but it is observable on every textual tool
///     result, not only on binary ones.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
///     <para>
///     <b>The constructed tool is a transparent wrapper around the factory's function.</b> It
///     forwards every piece of metadata — name, description, schemas, underlying method,
///     additional properties and service lookup — and differs only in result handling: the
///     oversized-output marker from <see cref="ToolResult.Oversized"/> is resolved to the refusal
///     it carries, or, when the tool is composed with retention of large outputs (see
///     <see cref="ToolOutputSpill"/>), an oversized result is retained and previewed.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Building tools with the guarded factory — the only supported construction path. A tool
///     returns a union (a refusal, or text, or structured data), so its delegate is declared to
///     return <see cref="object"/>; a no-path tool is built exactly the same way as a path-taking
///     one.
///     </para>
///     <code>
///     // A no-path tool: it takes no arguments and consults no policy, yet it is still built
///     // through the guarded factory, which validates the name and requires a description.
///     var now = () => ToolResult.Structured(new { utcTime = DateTimeOffset.UtcNow.ToString("O") });
///     var clock = GuardedToolFactory.Create(
///         now,
///         name: "clock_now",
///         description: "Reports the current UTC time. Takes no arguments.");
///
///     // A path-taking tool returns a refusal or a result, so its delegate returns object too.
///     // An omitted path is a discovery request, not an error; a path outside every granted
///     // location is refused with ToolResult.Denied rather than thrown.
///     var root = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(root, [PathRule.ReadOnly(root)]);
///
///     var wordCount = (string? path) =>
///     {
///         if (PathPolicy.IsDiscoveryRequest(path))
///             return ToolResult.Structured(new { inspectableLocations = policy.DiscoveryRoots() });
///
///         if (!policy.TryResolveRead(path!, out var realPath, out var denialMessage))
///             return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
///
///         return ToolResult.Structured(new { path = policy.EmitRelative(realPath, path) });
///     };
///
///     var docstats = GuardedToolFactory.Create(
///         wordCount,
///         name: "docstats_wordcount",
///         description: "Counts the words in a text file. Omit the path to list inspectable locations.");
///     </code>
/// </example>
public static class GuardedToolFactory
{
    /// <summary>
    ///     Creates a tool that delivers its result to the runtime in the form the tool returned
    ///     it, serializing only what a provider could not otherwise read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The result-delivery guard is supplied internally and cannot be omitted or replaced.
    ///     It matters because the underlying factory decides whether to serialize a result by
    ///     the delegate's <em>declared</em> return type: a tool declared to return
    ///     <see cref="object"/> — which every tool returning a <see cref="ToolResult"/>
    ///     necessarily is — would otherwise have its content flattened into JSON, and the
    ///     provider would never see the image the tool returned. See the type-level remarks
    ///     before concluding this guard is unnecessary.
    ///     </para>
    ///     <para>
    ///     The options object the underlying factory consumes is never accepted from a caller.
    ///     It is a mutable type carrying a settable result-marshaling hook, so accepting one
    ///     would mean either mutating the caller's instance or silently discarding their hook;
    ///     either way the guard would become negotiable, which is exactly what this unit exists
    ///     to prevent.
    ///     </para>
    ///     <para>
    ///     Both <paramref name="name"/> and <paramref name="description"/> are mandatory. With
    ///     no name the underlying factory derives a compiler-generated identifier from the
    ///     delegate, which is meaningless to a model, unstable across recompilation, and would
    ///     violate the naming convention; with no description the model is left guessing what
    ///     the tool does. They are the model's only means of choosing the tool.
    ///     </para>
    /// </remarks>
    /// <param name="method">
    ///     The tool implementation. Any delegate is accepted — synchronous or asynchronous,
    ///     with or without parameters — and its parameters determine the tool's JSON schema.
    ///     Must not be <see langword="null"/>.
    /// </param>
    /// <param name="name">
    ///     The tool name presented to the model. Must satisfy the naming convention; see
    ///     <see cref="ToolName.Validate"/>.
    /// </param>
    /// <param name="description">
    ///     The description presented to the model. Must be non-null and non-empty.
    /// </param>
    /// <param name="serializerOptions">
    ///     Options governing generation of the tool's <em>parameter</em> schema, or
    ///     <see langword="null"/> for the factory default. This is the only configuration this
    ///     method accepts, and it is deliberately one that cannot weaken result delivery.
    /// </param>
    /// <returns>The constructed tool, carrying the validated name and the description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="method"/>, <paramref name="name"/> or
    ///     <paramref name="description"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="description"/> is empty, or when
    ///     <paramref name="name"/> does not satisfy the naming convention.
    /// </exception>
    public static AIFunction Create(
        Delegate method,
        string name,
        string description,
        JsonSerializerOptions? serializerOptions = null)
    {
        // A tool with no implementation is a programming error; there is nothing to guard.
        ArgumentNullException.ThrowIfNull(method);

        // An empty description leaves the model no basis on which to choose this tool.
        ArgumentException.ThrowIfNullOrEmpty(description);

        // Validate the name before anything is constructed, so a colliding or malformed name is
        // refused at the point the developer wrote it rather than reaching a model.
        ToolName.Validate(name);

        // Build the options internally. The result-marshaling hook is the whole point of this
        // unit: it preserves the shapes a provider must receive intact and serializes anything
        // else exactly as the factory would have. The lambda closes over the serializer options
        // because the serialization branch needs them, so it is not static; the capture is one
        // reference per constructed tool, which is the cost of not guessing at the options.
        var function = AIFunctionFactory.Create(
            method,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = serializerOptions,
                MarshalResult = (result, _, _) =>
                    new ValueTask<object?>(MarshalResult(result, serializerOptions))
            });

        // Wrap the factory's function so an oversized-output marker can never reach the runtime:
        // with no retention composed, the wrapper turns it into the refusal the tool would always
        // have given. The wrapper forwards every piece of metadata, so the tool a caller receives
        // is indistinguishable from the factory's own except in that one respect.
        return new GuardedFunction(function);
    }

    /// <summary>
    ///     Bounds any <see cref="AIFunction"/> — including one this library did not build — by the
    ///     result-character and binary-byte ceilings of a <see cref="ToolLimits"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why it exists.</b> AgentKit's own tools enforce their ceilings internally, so an
    ///     application mixing them with a tool it wrote itself, or one from another library, had no
    ///     way to bound the foreign tool: its oversized result reached the provider unbounded. A
    ///     300-row report tool returned 6,350 characters against a 1,500-character limit. This
    ///     extension puts the same ceilings around any function, so an application can apply one
    ///     policy to every tool it hands an agent, in any agent runtime.
    ///     </para>
    ///     <para>
    ///     <b>What is enforced.</b> After the function returns, a sequence of content that is not
    ///     already a list is enumerated once and delivered as that list, and a structured value — a
    ///     record, an anonymous object, a collection — is serialized to a JSON document with the
    ///     function's own <see cref="AIFunction.JsonSerializerOptions"/>, exactly as
    ///     <see cref="Create"/> serializes it. The result is then refused with
    ///     <see cref="DenialReason.ResourceTooLarge"/>, naming the size and the ceiling, when its
    ///     binary content exceeds <see cref="ToolLimits.MaxBinaryBytes"/> in total or its text — a
    ///     string, a JSON document's text, text content, or the text of a list made only of text
    ///     content — exceeds <see cref="ToolLimits.MaxResultCharacters"/>. Anything else is returned
    ///     unchanged. A refusal is a result, never an exception, so the agent's turn continues.
    ///     </para>
    ///     <para>
    ///     <b>What is not enforced.</b> <see cref="ToolLimits.MaxReadBytes"/>,
    ///     <see cref="ToolLimits.MaxAgentDepth"/>, <see cref="ToolLimits.MaxImagePixels"/> and
    ///     <see cref="ToolLimits.MaxDeleteEntries"/> govern how a tool does its work — what it reads,
    ///     delegates, decodes or deletes — which an opaque function's wrapper cannot observe. Only
    ///     the result is bounded. A tool that must not perform an unbounded operation must be
    ///     written to bound it.
    ///     </para>
    ///     <para>
    ///     <b>Composition with retention.</b> When the guarded function is later wrapped by a
    ///     <see cref="ToolOutputRetention"/> (or <see cref="ToolOutputSpill.Apply"/>), an oversized
    ///     text result is retained and previewed instead of refused; the binary ceiling still
    ///     refuses, because retention keeps text only. Applying this extension to a function it
    ///     already guarded replaces the ceilings rather than wrapping twice.
    ///     </para>
    ///     <para>
    ///     <b>Transparency.</b> The returned function forwards the name, description, parameter and
    ///     return schemas, serializer options, underlying method, additional properties and service
    ///     lookup of the function it wraps, so a wrapped
    ///     <see cref="ApprovalRequiredAIFunction"/> is still discovered through
    ///     <see cref="AITool.GetService(Type, object?)"/>. Nothing is validated about the name: a foreign
    ///     function keeps the name it has.
    ///     </para>
    ///     <para>
    ///     The returned function is immutable and as safe for concurrent invocation as the function
    ///     it wraps.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <para>
    ///     Bounding a tool built with <see cref="AIFunctionFactory"/> rather than with this factory.
    ///     </para>
    ///     <code>
    ///     AIFunction report = AIFunctionFactory.Create(
    ///         (int rows) => Enumerable.Range(1, rows).Select(n => new { row = n, square = n * n }),
    ///         name: "report_rows",
    ///         description: "Returns one row per number with its square.");
    ///
    ///     // A 300-row report serializes to far more than 1,500 characters, so the guarded tool
    ///     // returns "Denied (ResourceTooLarge): ..." naming both figures instead of the rows.
    ///     AIFunction bounded = report.AsGuarded(new ToolLimits(maxResultCharacters: 1_500));
    ///     </code>
    /// </example>
    /// <param name="function">The function to bound. Must not be <see langword="null"/>.</param>
    /// <param name="limits">The ceilings to enforce. Must not be <see langword="null"/>.</param>
    /// <returns>
    ///     A function with the same metadata that enforces <paramref name="limits"/> on every result.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="function"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    public static AIFunction AsGuarded(this AIFunction function, ToolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(limits);

        // A function this unit already guarded is re-wrapped over its inner function with the new
        // ceilings, so a second application replaces rather than stacks them.
        return function is GuardedFunction guarded
            ? guarded.WithLimits(limits)
            : new GuardedFunction(function, spill: null, limits);
    }

    /// <summary>
    ///     Delivers a tool's result to the runtime, preserving the shapes a provider must
    ///     receive intact and serializing everything else as the underlying factory would.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Blanket passthrough is not a safe guard.</b> It preserves content, which is the
    ///     point — but it also hands the provider whatever else a tool happens to return. A tool
    ///     returning an anonymous object was observed to reach the provider as a raw CLR
    ///     instance, reported back as a compiler-generated type name, which no provider can
    ///     interpret. Selecting by shape keeps the property the guard exists for and removes
    ///     that failure.
    ///     </para>
    ///     <para>
    ///     The preserved shapes are exactly the ones a provider recognizes:
    ///     <see langword="null"/> (nothing to deliver), <see cref="string"/> (text and every
    ///     refusal), a single <see cref="AIContent"/>, and a sequence of
    ///     <see cref="AIContent"/> — the caption-plus-attachment shape the result constructors
    ///     produce. Anything else is structured data, and structured data belongs in JSON.
    ///     </para>
    ///     <para>
    ///     Serialization goes through <see cref="SerializeStructured"/>, which uses a
    ///     <see cref="JsonTypeInfo"/> obtained from the options rather than the reflection-based
    ///     overload, so that the unit carries no trimming or ahead-of-time compilation warning into
    ///     a consumer's build.
    ///     </para>
    ///     <para>
    ///     The oversized-output marker produced by <see cref="ToolResult.Oversized"/> is passed
    ///     through as well, so that the wrapper around the function can resolve it. Serializing it
    ///     here would hand a provider a JSON rendering of a whole output the ceiling exists to
    ///     keep out of the context window.
    ///     </para>
    /// </remarks>
    /// <param name="result">The value the tool's delegate returned; may be null.</param>
    /// <param name="serializerOptions">
    ///     The options the caller supplied, or <see langword="null"/> for the factory default.
    /// </param>
    /// <returns>
    ///     The result unchanged when its shape is one a provider recognizes or is the
    ///     oversized-output marker; otherwise a <see cref="JsonElement"/> holding its serialized
    ///     form.
    /// </returns>
    private static object? MarshalResult(object? result, JsonSerializerOptions? serializerOptions)
    {
        // Nothing to deliver, text, one content part, or a sequence of content parts: all four
        // are shapes the runtime and the provider already understand, and all four are handed
        // on untouched. This is the property the guard exists to hold. The oversized marker is
        // handed on too, because only the wrapper knows whether retention was composed.
        if (result is null or string or AIContent or IEnumerable<AIContent> or OversizedResult)
        {
            return result;
        }

        // Everything else is structured data. Serialize it exactly as the underlying factory
        // would have done, so that a tool returning a record or an anonymous object reaches the
        // model as readable JSON rather than as an opaque CLR instance.
        return SerializeStructured(result, serializerOptions);
    }

    /// <summary>
    ///     Serializes a structured result to a JSON document exactly as the underlying function
    ///     factory would.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Serialization goes through a <see cref="JsonTypeInfo"/> obtained from the options
    ///     rather than through the reflection-based overload, so that the unit carries no
    ///     trimming or ahead-of-time compilation warning into a consumer's build.
    ///     </para>
    ///     <para>
    ///     Shared with <see cref="ToolOutputSpill"/>, so a structured result from a function this
    ///     factory did not build is serialized identically when retention is composed. Any
    ///     exception the serializer raises — an unsupported type, a cycle — propagates.
    ///     </para>
    /// </remarks>
    /// <param name="value">The structured value to serialize. Must not be null.</param>
    /// <param name="serializerOptions">
    ///     The options to serialize with, or <see langword="null"/> for
    ///     <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// </param>
    /// <returns>The JSON document the value serializes to.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the options cannot describe or serialize the value's type.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the options cannot build a valid contract for the value's type, such as one
    ///     whose JSON property names collide, or when reading a member of the value throws it, as a
    ///     stream's timeout properties do.
    /// </exception>
    /// <exception cref="JsonException">
    ///     Thrown when the value's object graph cannot be written, such as one containing a cycle.
    /// </exception>
    internal static JsonElement SerializeStructured(object value, JsonSerializerOptions? serializerOptions)
    {
        var options = serializerOptions ?? AIJsonUtilities.DefaultOptions;
        return JsonSerializer.SerializeToElement(value, options.GetTypeInfo(value.GetType()));
    }
}

/// <summary>
///     The tool a guarded construction hands back: a transparent wrapper that resolves an
///     oversized-output marker, enforces result ceilings when <see cref="GuardedToolFactory.AsGuarded"/>
///     supplied them, and, when retention of large outputs is composed, hands oversized results to
///     that retention.
/// </summary>
/// <remarks>
///     <para>
///     <b>The wrapper is transparent by construction.</b> It derives from
///     <see cref="DelegatingAIFunction"/>, which forwards the name, the description, the
///     parameter and return schemas, the serializer options, the underlying method, the
///     additional properties and service lookup to the wrapped function. Every consumer in this
///     library and in the provider adapters reads only those members and invokes the tool, so
///     wrapping changes nothing a consumer can observe except the result handling below.
///     </para>
///     <para>
///     <b>Without retention composed</b>, the marker produced by <see cref="ToolResult.Oversized"/>
///     is replaced by exactly <c>Denied(DenialReason.ResourceTooLarge, message)</c> — the text the
///     tool gave before the marker existed. When ceilings are carried, a result is first normalized
///     (see <see cref="ResultMeasure.Normalize"/>) and refused with
///     <see cref="DenialReason.ResourceTooLarge"/> when its binary content or its text exceeds them;
///     every other result passes through untouched. A marker can therefore never reach a provider.
///     </para>
///     <para>
///     <b>With retention composed</b>, a result whose binary content exceeds carried ceilings is
///     still refused, because retention keeps text only; every other result is handed, with this
///     tool's serializer options, to the composition's attachment, which serializes a value that is
///     not text, JSON or content to a JSON document exactly as the guarded factory does — for a tool
///     the factory built this has already happened — retains an oversized output and returns a
///     preview naming its reference, delivers a sequence of content that is not already a list as
///     the list it was enumerated into — the same parts in the same order — and returns everything
///     else, a list or an array included, as it arrived. A value the serializer cannot handle fails
///     the invocation with the serializer's exception, as it would from the guarded factory. The
///     attachment is added through <see cref="WithSpill"/>, which builds a new wrapper over the same
///     inner function rather than mutating this one, so a tool list composed without retention is
///     never altered by a later composition that has it.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use; the attachment they may carry is
///     itself safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class GuardedFunction : DelegatingAIFunction
{
    /// <summary>
    ///     The retention this tool hands oversized results to, or <see langword="null"/> when the
    ///     composition does not retain large outputs.
    /// </summary>
    private readonly SpillAttachment? _spill;

    /// <summary>
    ///     The ceilings this tool enforces on its results, or <see langword="null"/> when the tool
    ///     bounds itself, as every tool the guarded factory built does.
    /// </summary>
    private readonly ToolLimits? _limits;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GuardedFunction"/> class.
    /// </summary>
    /// <param name="innerFunction">The function to wrap. Must not be null.</param>
    /// <param name="spill">
    ///     The retention oversized results are handed to, or <see langword="null"/> for none.
    /// </param>
    /// <param name="limits">
    ///     The ceilings to enforce on every result, or <see langword="null"/> for none.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="innerFunction"/> is <see langword="null"/>.
    /// </exception>
    internal GuardedFunction(AIFunction innerFunction, SpillAttachment? spill = null, ToolLimits? limits = null)
        : base(innerFunction)
    {
        _spill = spill;
        _limits = limits;
    }

    /// <summary>
    ///     Creates a wrapper over the same inner function that hands oversized results to the
    ///     supplied retention.
    /// </summary>
    /// <remarks>
    ///     A new instance rather than a mutation, so a tool already handed to a provider or held
    ///     in another composition keeps the behavior it was composed with. Any retention this
    ///     instance already carried is replaced rather than chained: a result is retained in one
    ///     place, the composition that offered the tool. Ceilings this instance carries are kept.
    /// </remarks>
    /// <param name="spill">The retention to hand oversized results to. Must not be null.</param>
    /// <returns>The new wrapper.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="spill"/> is <see langword="null"/>.
    /// </exception>
    internal GuardedFunction WithSpill(SpillAttachment spill)
    {
        ArgumentNullException.ThrowIfNull(spill);
        return new GuardedFunction(InnerFunction, spill, _limits);
    }

    /// <summary>
    ///     Creates a wrapper over the same inner function that enforces the supplied ceilings.
    /// </summary>
    /// <remarks>
    ///     Replaces any ceilings this instance carried rather than stacking a second wrapper, and
    ///     keeps any retention it carried.
    /// </remarks>
    /// <param name="limits">The ceilings to enforce. Must not be null.</param>
    /// <returns>The new wrapper.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="limits"/> is <see langword="null"/>.
    /// </exception>
    internal GuardedFunction WithLimits(ToolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new GuardedFunction(InnerFunction, _spill, limits);
    }

    /// <summary>
    ///     Invokes the wrapped function and resolves its result for delivery to the runtime.
    /// </summary>
    /// <param name="arguments">The arguments the runtime supplied.</param>
    /// <param name="cancellationToken">A token that cancels the invocation.</param>
    /// <returns>
    ///     The wrapped function's result, with an oversized-output marker replaced by its refusal,
    ///     a result exceeding carried ceilings replaced by a refusal or, when retention is composed,
    ///     an oversized text result replaced by its preview; a sequence of content that is not
    ///     already a list delivered as the list it was enumerated into; and a structured result from
    ///     a function the factory did not build delivered as the JSON document it was serialized to
    ///     whenever ceilings or retention are carried.
    /// </returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown, when ceilings or retention are carried, if the serializer options cannot
    ///     describe or serialize a structured result's type, exactly as the guarded factory throws.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown, when ceilings or retention are carried, if the serializer options cannot build a
    ///     valid contract for a structured result's type, such as one whose JSON property names
    ///     collide, or if reading a member of the result throws it, as a stream's timeout properties
    ///     do, exactly as the guarded factory throws.
    /// </exception>
    /// <exception cref="JsonException">
    ///     Thrown, when ceilings or retention are carried, if a structured result's object graph
    ///     cannot be written, such as one containing a cycle, exactly as the guarded factory throws.
    /// </exception>
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);

        // With ceilings carried, normalize once - a single-pass sequence becomes the list it was
        // enumerated into, structured data the JSON every adapter would send - and refuse binary
        // content over its ceiling. Retention keeps text only, so this refusal stands either way.
        if (_limits is not null)
        {
            result = ResultMeasure.Normalize(result, JsonSerializerOptions);

            var bytes = ResultMeasure.BinaryBytes(result);
            if (bytes > _limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The output of '" + Name + "' carries "
                    + bytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes of binary content, which exceeds the "
                    + _limits.MaxBinaryBytes.ToString(CultureInfo.InvariantCulture) + "-byte limit.");
            }
        }

        // With retention composed, the attachment decides, using this tool's serializer options
        // for any structured result: a structured result is serialized as the guarded factory
        // would, an oversized output is retained and previewed, a sequence of content that is not
        // already a list is delivered as the list it was enumerated into - the same parts in the
        // same order - and anything else is returned as it arrived.
        if (_spill is not null)
        {
            return _spill.Deliver(Name, JsonSerializerOptions, result);
        }

        // Without retention, a marker becomes the refusal the tool always gave, so a host that
        // has not opted in observes no change and no marker ever reaches a provider.
        if (result is OversizedResult oversized)
        {
            return oversized.ToDenial();
        }

        // With ceilings carried and no retention, text over the result ceiling is refused with
        // the size and the ceiling as plain facts - never truncated, which would hide what the
        // model never knew to ask for.
        if (_limits is not null
            && ResultMeasure.TryGetText(result, out _, out var delivered)
            && delivered > _limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The output of '" + Name + "' is "
                + delivered.ToString(CultureInfo.InvariantCulture)
                + " characters, which exceeds the "
                + _limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return result;
    }
}

/// <summary>
///     The measurements and normalization every result ceiling and the retention of large outputs
///     share, so a result is judged identically wherever it is bounded.
/// </summary>
/// <remarks>
///     Stateless and safe for concurrent use.
/// </remarks>
internal static class ResultMeasure
{
    /// <summary>
    ///     Brings a result to the form it is measured and delivered in.
    /// </summary>
    /// <remarks>
    ///     A sequence of content that is not already a list is enumerated exactly once and replaced
    ///     by that list, because enumerating it to measure and again in a provider's adapter would
    ///     deliver it empty or altered. A value that is not text, JSON or content — a record, an
    ///     anonymous object, a collection, a number — is serialized to a JSON document with the
    ///     function's own options, exactly as the guarded factory would, so it is measured as the
    ///     JSON every adapter sends. Everything else, the oversized-output marker included, is
    ///     returned as it arrived. Idempotent: normalizing a normalized result changes nothing.
    /// </remarks>
    /// <param name="result">The result a function returned; may be null.</param>
    /// <param name="serializerOptions">
    ///     The function's serializer options, or <see langword="null"/> for
    ///     <see cref="AIJsonUtilities.DefaultOptions"/>.
    /// </param>
    /// <returns>The normalized result.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the serializer options cannot describe or serialize a structured value's type.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the serializer options cannot build a valid contract for a structured value's
    ///     type, or when reading a member of the value throws it.
    /// </exception>
    /// <exception cref="JsonException">
    ///     Thrown when a structured value's object graph cannot be written, such as one with a cycle.
    /// </exception>
    internal static object? Normalize(object? result, JsonSerializerOptions? serializerOptions)
    {
        if (result is IEnumerable<AIContent> sequence and not IReadOnlyList<AIContent>)
        {
            return sequence.ToList();
        }

        if (result is not null and not (string or JsonElement or AIContent or IEnumerable<AIContent> or OversizedResult))
        {
            return GuardedToolFactory.SerializeStructured(result, serializerOptions);
        }

        return result;
    }

    /// <summary>
    ///     Totals the bytes of binary content a normalized result carries.
    /// </summary>
    /// <param name="result">The normalized result; a sequence of content must already be a list.</param>
    /// <returns>The total bytes of every <see cref="DataContent"/> in the result, or zero.</returns>
    internal static long BinaryBytes(object? result)
    {
        return result switch
        {
            DataContent data => data.Data.Length,
            IReadOnlyList<AIContent> parts => parts.OfType<DataContent>().Sum(part => (long)part.Data.Length),
            _ => 0
        };
    }

    /// <summary>
    ///     Extracts the text of a result that is purely textual, and measures the result.
    /// </summary>
    /// <remarks>
    ///     A result is measured by its own representation - a string by its characters, any JSON
    ///     element by its JSON text, text content by its text - not by any envelope a provider adds
    ///     around it, which differs from provider to provider. A JSON string is measured by its JSON
    ///     text, quotes and escapes included, because that is the form the chat adapters send, and
    ///     it never under-counts an adapter that sends the decoded value instead. Its text is the
    ///     decoded value, which is what retention keeps: the retained copy is what a model pages by
    ///     line and searches literally, which an escaped single line defeats.
    /// </remarks>
    /// <param name="result">
    ///     The result to inspect. A sequence of content must already be a list, so it is enumerated
    ///     only by the caller.
    /// </param>
    /// <param name="text">On success, the text the result carries.</param>
    /// <param name="delivered">
    ///     On success, the characters the result's own representation occupies: what a ceiling
    ///     judges and a preview header reports.
    /// </param>
    /// <returns><see langword="true"/> when the result is purely textual.</returns>
    internal static bool TryGetText(object? result, out string text, out int delivered)
    {
        switch (result)
        {
            case string value:
                text = value;
                delivered = value.Length;
                return true;

            // An undefined element carries no value at all, so it is not text; it must reach the
            // runtime exactly as it would without a ceiling.
            case JsonElement { ValueKind: JsonValueKind.Undefined }:
                break;

            // A JSON string is text a function serialized: measured by its JSON text, carried
            // decoded so its lines can be paged and searched.
            case JsonElement { ValueKind: JsonValueKind.String } element:
                text = element.GetString() ?? string.Empty;
                delivered = element.GetRawText().Length;
                return true;

            case JsonElement element:
                text = element.GetRawText();
                delivered = text.Length;
                return true;

            case TextContent content:
                text = content.Text;
                delivered = text.Length;
                return true;

            // A sequence is textual only when every part is text; a caption beside an image is not.
            case IReadOnlyList<AIContent> parts:
                if (parts.Count == 0 || parts.Any(part => part is not TextContent))
                {
                    break;
                }

                text = string.Join("\n", parts.Cast<TextContent>().Select(part => part.Text));
                delivered = text.Length;
                return true;
        }

        text = string.Empty;
        delivered = 0;
        return false;
    }
}
