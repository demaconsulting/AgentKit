using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit ChatClient adapter's public API compiles
///     against the real API.
/// </summary>
/// <remarks>
///     The adapter's example is the final assembly step — chat client, tools, agent — so a consumer
///     who follows the documentation from <c>PathPolicy</c> through to here has a runnable
///     application. An example that named a framework member incorrectly would break exactly that
///     path, which is why it is compiled rather than reviewed.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the ChatClient adapter package compiles.
    /// </summary>
    [Fact]
    public void AgentKitAgentsChatClient_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Agents.ChatClient.xml",
            "DemaConsulting.AgentKit.Agents.ChatClient",
            "DemaConsulting.AgentKit.Core",
            "Microsoft.Agents.AI",
            "Microsoft.Extensions.AI");
    }
}
