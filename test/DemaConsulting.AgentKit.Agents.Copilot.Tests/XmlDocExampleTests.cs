using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Copilot adapter's public API compiles
///     against the real API.
/// </summary>
/// <remarks>
///     The adapter's example is the final assembly step — client, tools, agent — so a consumer who
///     follows the documentation from <c>PathPolicy</c> through to here has a runnable application.
///     An example that named a Copilot SDK member incorrectly would break exactly that path, which
///     is why it is compiled rather than reviewed.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Copilot adapter package compiles.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Agents.Copilot.xml",
            "DemaConsulting.AgentKit.Agents.Copilot",
            "DemaConsulting.AgentKit.Core",
            "GitHub.Copilot",
            "Microsoft.Agents.AI",
            "Microsoft.Extensions.AI");
    }
}
