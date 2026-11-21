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
///     oversized-output marker, and, when retention of large outputs is composed, hands oversized
///     results to that retention.
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
///     <b>Without retention composed</b>, the only change is that the marker produced by
///     <see cref="ToolResult.Oversized"/> is replaced by exactly
///     <c>Denied(DenialReason.ResourceTooLarge, message)</c> — the text the tool gave before the
///     marker existed — and every other result passes through untouched. A marker can therefore
///     never reach a provider.
///     </para>
///     <para>
///     <b>With retention composed</b>, every result is handed, with this tool's serializer
///     options, to the composition's attachment, which serializes a value that is not text, JSON
///     or content to a JSON document exactly as the guarded factory does — for a tool the factory
///     built this has already happened — retains an oversized output and returns a preview naming
///     its reference, delivers a sequence of content that is not already a list as the list it was
///     enumerated into — the same parts in the same order — and returns everything else, a list or
///     an array included, as it arrived. A value the serializer cannot handle fails the invocation
///     with the serializer's exception, as it would from the guarded factory. The attachment is
///     added by <see cref="ToolOutputSpill.Apply"/> through <see cref="WithSpill"/>, which builds a
///     new wrapper over the same inner function rather than mutating this one, so a tool list
///     composed without retention is never altered by a later composition that has it.
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
    ///     Initializes a new instance of the <see cref="GuardedFunction"/> class.
    /// </summary>
    /// <param name="innerFunction">The function to wrap. Must not be null.</param>
    /// <param name="spill">
    ///     The retention oversized results are handed to, or <see langword="null"/> for none.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="innerFunction"/> is <see langword="null"/>.
    /// </exception>
    internal GuardedFunction(AIFunction innerFunction, SpillAttachment? spill = null)
        : base(innerFunction)
    {
        _spill = spill;
    }

    /// <summary>
    ///     Creates a wrapper over the same inner function that hands oversized results to the
    ///     supplied retention.
    /// </summary>
    /// <remarks>
    ///     A new instance rather than a mutation, so a tool already handed to a provider or held
    ///     in another composition keeps the behavior it was composed with. Any retention this
    ///     instance already carried is replaced rather than chained: a result is retained in one
    ///     place, the composition that offered the tool.
    /// </remarks>
    /// <param name="spill">The retention to hand oversized results to. Must not be null.</param>
    /// <returns>The new wrapper.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="spill"/> is <see langword="null"/>.
    /// </exception>
    internal GuardedFunction WithSpill(SpillAttachment spill)
    {
        ArgumentNullException.ThrowIfNull(spill);
        return new GuardedFunction(InnerFunction, spill);
    }

    /// <summary>
    ///     Invokes the wrapped function and resolves its result for delivery to the runtime.
    /// </summary>
    /// <param name="arguments">The arguments the runtime supplied.</param>
    /// <param name="cancellationToken">A token that cancels the invocation.</param>
    /// <returns>
    ///     The wrapped function's result, with an oversized-output marker replaced by its refusal
    ///     or, when retention is composed, an oversized result replaced by its preview and a
    ///     sequence of content that is not already a list delivered as the list it was enumerated
    ///     into, and a structured result from a function the factory did not build delivered as
    ///     the JSON document it was serialized to.
    /// </returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown, when retention is composed, if the serializer options cannot describe or
    ///     serialize a structured result's type, exactly as the guarded factory throws.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown, when retention is composed, if the serializer options cannot build a valid
    ///     contract for a structured result's type, such as one whose JSON property names collide,
    ///     or if reading a member of the result throws it, as a stream's timeout properties do,
    ///     exactly as the guarded factory throws.
    /// </exception>
    /// <exception cref="JsonException">
    ///     Thrown, when retention is composed, if a structured result's object graph cannot be
    ///     written, such as one containing a cycle, exactly as the guarded factory throws.
    /// </exception>
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);

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
        return result is OversizedResult oversized ? oversized.ToDenial() : result;
    }
}
