using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     Builds a Microsoft Agent Framework <see cref="AIAgent"/> from any
///     <see cref="IChatClient"/> and a supplied tool list, installing AgentKit Core's
///     <see cref="ImagePromotingChatClient"/> automatically and unconditionally.
/// </summary>
/// <remarks>
///     <para>
///     A provider reached through an <see cref="IChatClient"/> preserves an image a tool returns
///     all the way through the framework and then drops it at the wire, because that interface
///     accepts images on messages rather than in tool responses. The model answers anyway,
///     describing an image it never received, and nothing in the exchange reports an error.
///     </para>
///     <para>
///     <b>The decorator is not optional.</b> Forgetting to install it breaks images silently, so
///     this factory installs <see cref="ImagePromotingChatClient"/> on every agent it builds and
///     exposes no parameter to disable it — omission is made impossible rather than merely
///     discouraged. The decorator is placed beneath the agent's function-invocation loop, where
///     it observes the conversation after tool results have been appended, which is the only place
///     a tool-returned image exists to be promoted.
///     </para>
///     <para>
///     This factory is a thin adapter: beyond validation and the unconditional decorator, its only
///     job is to construct a <see cref="ChatClientAgent"/>. It holds no state and shares no code
///     with the Copilot adapter. A host that builds its own agent or pipeline installs the same
///     decorator through <see cref="UseImagePromotion"/>.
///     </para>
/// </remarks>
public static class ChatClientAgentFactory
{
    /// <summary>
    ///     Builds an agent from a chat client and a supplied tool list, with the image-promoting
    ///     decorator installed beneath the function-invocation loop.
    /// </summary>
    /// <param name="client">
    ///     The chat client the agent talks to. Must not be <see langword="null"/>. The client is
    ///     wrapped in an <see cref="ImagePromotingChatClient"/> before use; the caller retains
    ///     ownership of the client it supplied.
    /// </param>
    /// <param name="tools">
    ///     The tools the agent may call. Must not be <see langword="null"/> or empty, must contain
    ///     no <see langword="null"/> entry, and must carry no two tools of the same name.
    /// </param>
    /// <param name="instructions">The system instructions for the agent, if any.</param>
    /// <param name="name">The name of the agent, if any.</param>
    /// <param name="selection">
    ///     The validated model selection to apply to every request the agent sends, or
    ///     <see langword="null"/> to send requests as the client is configured. When supplied, the
    ///     client is wrapped in a <see cref="ModelSettingsChatClient"/> beneath the image-promoting
    ///     decorator, so the model, the reasoning effort and the output limit ride on every request.
    ///     A selection carrying a context tier, or an effort with no provider-neutral equivalent, is
    ///     refused, because an <see cref="IChatClient"/> cannot apply either.
    /// </param>
    /// <returns>An agent that talks to the supplied client through the image-promoting decorator.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="tools"/> is <see langword="null"/>, or a
    ///     tool in <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name, or
    ///     <paramref name="selection"/> carries a setting an <see cref="IChatClient"/> cannot apply.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Assembling a complete agent on any <see cref="IChatClient"/> provider. The policy decides
    ///     where the tools may go, the builder composes the tool list, and this factory turns the
    ///     client plus that list into a runnable agent — the image-promoting decorator is installed
    ///     for you. In a real application <c>textFiles</c> and <c>images</c>
    ///     are <c>new TextFilePack()</c> and <c>new ImagePack()</c> from the
    ///     <c>DemaConsulting.AgentKit.Tools</c> package.
    ///     </para>
    ///     <code>
    ///     public AIAgent CreateAgent(
    ///         IChatClient client,
    ///         string workspace,
    ///         string session,
    ///         IToolPack textFiles,
    ///         IToolPack images)
    ///     {
    ///         // The anchor is the workspace; the grants say what may be read and what may be written.
    ///         var policy = new PathPolicy(
    ///             workingDirectory: workspace,
    ///             grants: [PathRule.ReadOnly(workspace), PathRule.ReadWrite(session)]);
    ///
    ///         IList&lt;AIFunction&gt; tools =
    ///         [
    ///             .. new ToolPackBuilder(policy)
    ///                 .WithHostCapabilities(HostCapabilities.Vision)
    ///                 .Add(textFiles)
    ///                 .Add(images)
    ///                 .Build()
    ///         ];
    ///
    ///         return ChatClientAgentFactory.Create(
    ///             client,
    ///             tools,
    ///             instructions: "You are a document assistant confined to the permitted locations.",
    ///             name: "document-assistant");
    ///     }
    ///     </code>
    ///     <para>
    ///     Running the agent on a model the user chose from a catalog. The selection is validated
    ///     before it gets here, its model's vision capability decides whether the image family is
    ///     offered, and its settings ride on every request.
    ///     </para>
    ///     <code>
    ///     public AIAgent CreateAgent(
    ///         IChatClient client,
    ///         ModelSelection selection,
    ///         PathPolicy policy,
    ///         IToolPack textFiles,
    ///         IToolPack images)
    ///     {
    ///         IList&lt;AIFunction&gt; tools =
    ///         [
    ///             .. new ToolPackBuilder(policy)
    ///                 .WithHostCapabilities(selection.HostCapabilities)
    ///                 .Add(textFiles)
    ///                 .Add(images)
    ///                 .Build()
    ///         ];
    ///
    ///         return ChatClientAgentFactory.Create(client, tools, selection: selection);
    ///     }
    ///     </code>
    /// </example>
    public static AIAgent Create(
        IChatClient client,
        IList<AIFunction> tools,
        string? instructions = null,
        string? name = null,
        ModelSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateTools(tools);

        // The selection sits directly over the provider's client, beneath image promotion, so every
        // request the agent's loop sends - including the ones after a tool ran - carries it.
        var configured = selection is null ? client : new ModelSettingsChatClient(client, selection);
        var promotingClient = WrapWithImagePromotion(configured);

        return new ChatClientAgent(
            promotingClient,
            instructions: instructions,
            name: name,
            tools: [.. tools.Cast<AITool>()]);
    }

    /// <summary>
    ///     Installs AgentKit Core's <see cref="ImagePromotingChatClient"/> in a chat client pipeline a
    ///     host builds itself, so a tool-returned image reaches the model on a provider whose tool
    ///     response channel cannot carry one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>When it is needed.</b> Required when the provider adapter cannot deliver an image a
    ///     tool returned: with Ollama through OllamaSharp, a tool-result image is serialized as
    ///     base64 text into the tool message and the model never sees pixels, so the decorator moves
    ///     the image onto a following user message and leaves a short placeholder in the tool result.
    ///     Not needed with the GitHub Copilot SDK, which delivers tool-result images to the model
    ///     natively; AgentKit's Copilot adapter does not install it.
    ///     </para>
    ///     <para>
    ///     <b>Where it must sit.</b> Beneath the function-invocation loop, where the conversation
    ///     already holds the tool results. For a host-built <see cref="ChatClientAgent"/>, which adds
    ///     its own function-invocation loop above the client it is given, build
    ///     <c>client.AsBuilder().UseImagePromotion().Build()</c> and pass that to the agent. For a raw
    ///     pipeline, call it after <c>UseFunctionInvocation()</c>:
    ///     <c>client.AsBuilder().UseFunctionInvocation().UseImagePromotion().Build()</c>. Placed above
    ///     the loop it would only ever see the request before any tool ran, so that placement is
    ///     refused when the pipeline is built rather than left to fail silently.
    ///     </para>
    ///     <para>
    ///     <see cref="Create"/> installs the decorator itself; this extension is for a pipeline the
    ///     host assembles. A host that takes no dependency on this package can install the same
    ///     decorator with <c>builder.Use(inner =&gt; new ImagePromotingChatClient(inner))</c>.
    ///     </para>
    /// </remarks>
    /// <param name="builder">The pipeline being built. Must not be <see langword="null"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown by <see cref="ChatClientBuilder.Build(IServiceProvider?)"/> when the client beneath
    ///     the decorator already contains a <see cref="FunctionInvokingChatClient"/> — that is, when
    ///     <c>UseImagePromotion()</c> was called before <c>UseFunctionInvocation()</c>.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Installing image promotion in a pipeline the host builds, for a host-built agent and for
    ///     a raw chat client.
    ///     </para>
    ///     <code>
    ///     public AIAgent CreateAgent(IChatClient client, IList&lt;AITool&gt; tools)
    ///     {
    ///         // ChatClientAgent adds its function-invocation loop above the client it is given,
    ///         // so image promotion sits beneath that loop.
    ///         IChatClient promoting = client.AsBuilder().UseImagePromotion().Build();
    ///         return new ChatClientAgent(promoting, instructions: "You read images.", tools: tools);
    ///     }
    ///
    ///     public IChatClient CreatePipeline(IChatClient client) =>
    ///         client.AsBuilder()
    ///             .UseFunctionInvocation()
    ///             .UseImagePromotion()
    ///             .Build();
    ///     </code>
    /// </example>
    public static ChatClientBuilder UseImagePromotion(this ChatClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use(inner =>
        {
            // The pipeline is composed outermost first, so this factory receives everything beneath
            // the decorator. A function-invocation loop down there means the decorator would sit
            // above it and never see a tool result.
            if (inner.GetService(typeof(FunctionInvokingChatClient)) is not null)
            {
                throw new InvalidOperationException(
                    "Image promotion must sit beneath function invocation: call UseFunctionInvocation() "
                    + "before UseImagePromotion(), or pass the built client to a ChatClientAgent, which "
                    + "adds function invocation above it.");
            }

            return WrapWithImagePromotion(inner);
        });
    }

    /// <summary>
    ///     Installs AgentKit Core's <see cref="ModelSettingsChatClient"/> in a chat client pipeline a
    ///     host builds itself, so a validated model selection rides on every request.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <see cref="Create"/> installs the decorator itself when handed a selection; this extension
    ///     is for a pipeline the host assembles. Its position is not critical — a value already set on
    ///     a request wins over the selection wherever the decorator sits — but placing it last, nearest
    ///     the provider, keeps every request the pipeline makes under the selection.
    ///     </para>
    ///     <para>
    ///     A host that takes no dependency on this package installs the same decorator with
    ///     <c>builder.Use(inner =&gt; new ModelSettingsChatClient(inner, selection))</c>.
    ///     </para>
    /// </remarks>
    /// <param name="builder">The pipeline being built. Must not be <see langword="null"/>.</param>
    /// <param name="selection">The validated selection to apply. Must not be <see langword="null"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder"/> or <paramref name="selection"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the pipeline is built if <paramref name="selection"/> carries a context tier or
    ///     an effort with no provider-neutral equivalent, which an <see cref="IChatClient"/> cannot
    ///     apply.
    /// </exception>
    /// <example>
    ///     <code>
    ///     public IChatClient CreatePipeline(IChatClient client, ModelSelection selection) =>
    ///         client.AsBuilder()
    ///             .UseFunctionInvocation()
    ///             .UseImagePromotion()
    ///             .UseModelSettings(selection)
    ///             .Build();
    ///     </code>
    /// </example>
    public static ChatClientBuilder UseModelSettings(this ChatClientBuilder builder, ModelSelection selection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(selection);

        return builder.Use(inner => new ModelSettingsChatClient(inner, selection));
    }

    /// <summary>
    ///     Wraps a chat client in the image-promoting decorator.
    /// </summary>
    /// <remarks>
    ///     Exposed as a seam so a test can assert the decorator is installed — the guarantee this
    ///     package exists for — without having to reach inside a constructed agent. The wrap is
    ///     unconditional: there is no code path in this factory that produces an agent whose chat
    ///     client is not image-promoting.
    /// </remarks>
    /// <param name="client">The client to wrap. Must not be <see langword="null"/>.</param>
    /// <returns>An <see cref="ImagePromotingChatClient"/> forwarding to <paramref name="client"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    internal static IChatClient WrapWithImagePromotion(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new ImagePromotingChatClient(client);
    }

    /// <summary>
    ///     Rejects a tool list that a well-formed agent could not be built from.
    /// </summary>
    /// <remarks>
    ///     A missing, empty, or malformed tool list is a defect in the composing application,
    ///     surfaced where the host wrote it rather than as undefined behavior when a model tries
    ///     to select a tool. Two tools sharing a name would leave which one a model invokes
    ///     undefined, so the collision is refused here.
    /// </remarks>
    /// <param name="tools">The tool list to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static void ValidateTools(IList<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
        {
            throw new ArgumentException("At least one tool is required.", nameof(tools));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ArgumentNullException(nameof(tools), "A tool in the list is null.");
            }

            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException(
                    $"Two tools carry the name '{tool.Name}'; tool names must be unique.",
                    nameof(tools));
            }
        }
    }
}
