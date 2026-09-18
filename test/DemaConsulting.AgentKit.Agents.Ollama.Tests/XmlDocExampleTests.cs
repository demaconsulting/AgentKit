using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Ollama adapter's public API compiles
///     against the real API.
/// </summary>
/// <remarks>
///     The package's example is the one place a reader learns how to obtain the window before
///     configuring a session with it. An example that named an OllamaSharp member incorrectly would
///     break exactly that step, which is why it is compiled rather than reviewed.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Ollama adapter package compiles.
    /// </summary>
    [Fact]
    public void AgentKitAgentsOllama_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Agents.Ollama.xml",
            "DemaConsulting.AgentKit.Agents.Ollama",
            "Microsoft.Extensions.AI",
            "OllamaSharp");
    }
}
