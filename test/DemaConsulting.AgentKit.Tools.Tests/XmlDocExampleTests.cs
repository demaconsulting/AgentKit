using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Tools.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Tools public API compiles against the
///     real API.
/// </summary>
/// <remarks>
///     The Tools examples are the ones that compose the shipped packs with a real policy, which is
///     the step Core's own examples cannot show because Core does not reference Tools. Verifying
///     them here is what proves an agent can assemble a working tool set from the documentation
///     alone.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Tools package compiles.
    /// </summary>
    [Fact]
    public void AgentKitTools_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Tools.xml",
            "DemaConsulting.AgentKit.Core",
            "DemaConsulting.AgentKit.Tools.Image",
            "DemaConsulting.AgentKit.Tools.TextFile",
            "Microsoft.Extensions.AI");
    }
}
