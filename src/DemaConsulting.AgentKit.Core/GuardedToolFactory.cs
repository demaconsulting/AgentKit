using System.Text.Json;
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
///     are consumable, so the change is benign — but it is observable on every tool result, not
///     only on binary ones.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class GuardedToolFactory
{
    /// <summary>
    ///     Creates a tool that delivers its result to the runtime in the form the tool returned
    ///     it.
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
        // unit: it returns the tool's result unchanged instead of letting the factory serialize
        // it to JSON. The lambda is static so no closure is allocated per tool.
        return AIFunctionFactory.Create(
            method,
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                SerializerOptions = serializerOptions,
                MarshalResult = static (result, _, _) => new ValueTask<object?>(result)
            });
    }
}
