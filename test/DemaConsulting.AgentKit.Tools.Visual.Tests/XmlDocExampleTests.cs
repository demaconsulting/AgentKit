using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Tools Visual public API compiles
///     against the real API.
/// </summary>
/// <remarks>
///     The Tools Visual examples compose the shipped image pack with a real policy, which is the
///     step Core's own examples cannot show because Core does not reference this package.
///     Verifying them here is what shows an agent can assemble a working tool set from the
///     documentation alone.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Tools Visual package compiles.
    /// </summary>
    [Fact]
    public void AgentKitToolsVisual_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Tools.Visual.xml",
            "DemaConsulting.AgentKit.Core",
            "DemaConsulting.AgentKit.Tools.Visual.Image",
            "Microsoft.Extensions.AI");
    }
}
