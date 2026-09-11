using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Builds a Microsoft Agent Framework <see cref="AIAgent"/> from a GitHub Copilot
///     <see cref="CopilotClient"/> and a supplied tool list, suppressing the built-in tools the
///     Copilot runtime would otherwise inject.
/// </summary>
/// <remarks>
///     <para>
///     Copilot arrives as a complete agent runtime carrying its own tools — shell, fetch, file
///     reading and writing, and more. Supplying an application's own tools is not enough: unless
///     the built-ins are suppressed, a document assistant silently retains command execution.
///     <c>SessionConfig.AvailableTools</c> is an allow-list, so this factory derives it from
///     the <em>same</em> collection it assigns to <c>SessionConfig.Tools</c>, which makes
///     the tool set and its allow-list impossible to drift apart. This derivation is the
///     safety-critical behavior of the package.
///     </para>
///     <para>
///     <b>Permission handling is default-safe.</b> When no handler is supplied, the factory
///     installs one that approves exactly the supplied tools by name and rejects every other
///     request — including every built-in tool, none of which is one of the supplied custom tools.
///     A host may supply its own handler to override that behavior.
///     </para>
///     <para>
///     <b>Ownership contract.</b> The host constructs, starts, and disposes the
///     <see cref="CopilotClient"/>; this factory takes no ownership of it. The agent is built with
///     <c>ownsClient: false</c>, and the factory creates nothing disposable of its own, so the
///     rule is unambiguous: <em>whoever created the client disposes it</em>.
///     </para>
///     <para>
///     <b>No image-promoting decorator.</b> Unlike the <c>IChatClient</c> adapter, this factory
///     does not install <c>ImagePromotingChatClient</c>: the Copilot runtime already delivers
///     images from tool results to the model, verified in the spike, so promoting them onto a user
///     message would duplicate content the model already received. A future maintainer should not
///     "helpfully" add it here.
///     </para>
///     <para>
///     This factory is a thin adapter. It holds no state and shares no code with the ChatClient
///     adapter.
///     </para>
/// </remarks>
public static class CopilotAgentFactory
{
    /// <summary>
    ///     Builds an agent from a Copilot client and a supplied tool list, with the runtime's
    ///     built-in tools suppressed.
    /// </summary>
    /// <param name="client">
    ///     The Copilot client the agent runs on. Must not be <see langword="null"/>. The host owns
    ///     the client: it constructs, starts, and disposes it, and this factory builds the agent
    ///     with <c>ownsClient: false</c>.
    /// </param>
    /// <param name="tools">
    ///     The tools the agent may call. Must not be <see langword="null"/> or empty, must contain
    ///     no <see langword="null"/> entry, and must carry no two tools of the same name. The
    ///     session's allow-list is derived from this collection.
    /// </param>
    /// <param name="instructions">The system instructions for the agent, if any.</param>
    /// <param name="onPermissionRequest">
    ///     The permission handler. When <see langword="null"/>, a safe default is installed that
    ///     approves exactly the supplied tools by name and rejects everything else.
    /// </param>
    /// <param name="name">The name of the agent, if any.</param>
    /// <returns>An agent that runs on the supplied client with only the supplied tools available.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="tools"/> is <see langword="null"/>, or a
    ///     tool in <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    public static AIAgent Create(
        CopilotClient client,
        IList<AIFunction> tools,
        string? instructions = null,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateTools(tools);

        var config = BuildSessionConfig(tools, instructions, onPermissionRequest);

        // ownsClient: false — the host owns the client's lifetime; see the ownership contract in
        // the type remarks. The SessionConfig overload of AsAIAgent is the only path that carries
        // the AvailableTools allow-list, and therefore the only one that suppresses the built-ins.
        return client.AsAIAgent(config, ownsClient: false, name: name);
    }

    /// <summary>
    ///     Builds the session configuration that suppresses the built-in tools, deriving the
    ///     allow-list from the supplied tools.
    /// </summary>
    /// <remarks>
    ///     Exposed as a seam so a test can assert the safety-critical property — that
    ///     <c>SessionConfig.AvailableTools</c> is derived from the same collection as
    ///     <c>SessionConfig.Tools</c> — without a live <see cref="CopilotClient"/>. Both are
    ///     built from <paramref name="tools"/> in one place so the two cannot diverge.
    /// </remarks>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <returns>The configured session.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static SessionConfig BuildSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest)
    {
        ValidateTools(tools);

        var config = new SessionConfig
        {
            // AvailableTools and Tools are both derived from the same collection so the tool set
            // and its allow-list cannot drift apart. This is the suppression the package exists for.
            Tools = [.. tools],
            AvailableTools = [.. tools.Select(tool => tool.Name)],

            // The runtime's skills are a further channel of injected capability an untrained user
            // could not adjudicate; a confined agent receives only what its host attached.
            EnableSkills = false,
            SkipCustomInstructions = true,

            OnPermissionRequest = onPermissionRequest ?? CreateDefaultPermissionHandler(tools),
        };

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = instructions,
            };
        }

        return config;
    }

    /// <summary>
    ///     Creates the safe-default permission handler: approve exactly the supplied tools by name,
    ///     reject everything else.
    /// </summary>
    /// <remarks>
    ///     A built-in request — shell, read, write, url and the rest — is never a
    ///     <see cref="PermissionRequestCustomTool"/>, so it can never match a supplied tool name
    ///     and is always rejected. Only a supplied custom tool, requested by its exact name, is
    ///     approved. The allow-list is captured once, so the handler cannot approve a name the
    ///     session did not publish.
    /// </remarks>
    /// <param name="tools">The tools whose names are approved.</param>
    /// <returns>A permission handler enforcing the allow-list.</returns>
    internal static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>
        CreateDefaultPermissionHandler(IList<AIFunction> tools)
    {
        var permitted = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.Ordinal);

        return (request, _) =>
        {
            if (request is PermissionRequestCustomTool custom && permitted.Contains(custom.ToolName))
            {
                return Task.FromResult(PermissionDecision.ApproveOnce());
            }

            var requested = DescribeRequest(request);
            return Task.FromResult(PermissionDecision.Reject($"'{requested}' is not an allowed tool."));
        };
    }

    /// <summary>
    ///     Names the tool a permission request concerns, for a rejection message and diagnostics.
    /// </summary>
    /// <param name="request">The request to describe.</param>
    /// <returns>The custom tool name, or the request kind for a built-in request.</returns>
    private static string DescribeRequest(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestCustomTool custom => custom.ToolName,
            _ => request.Kind ?? "(unknown)",
        };
    }

    /// <summary>
    ///     Rejects a tool list that a well-formed agent could not be built from.
    /// </summary>
    /// <remarks>
    ///     A missing, empty, or malformed tool list is a defect in the composing application. Two
    ///     tools sharing a name would make both the published tool set and the derived allow-list
    ///     ambiguous, so the collision is refused here.
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

