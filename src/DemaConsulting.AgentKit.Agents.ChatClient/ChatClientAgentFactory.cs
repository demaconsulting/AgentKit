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
///     with the Copilot adapter.
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
    /// <returns>An agent that talks to the supplied client through the image-promoting decorator.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="tools"/> is <see langword="null"/>, or a
    ///     tool in <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    public static AIAgent Create(
        IChatClient client,
        IList<AIFunction> tools,
        string? instructions = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateTools(tools);

        var promotingClient = WrapWithImagePromotion(client);

        return new ChatClientAgent(
            promotingClient,
            instructions: instructions,
            name: name,
            tools: [.. tools.Cast<AITool>()]);
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
