using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Core public API compiles against the
///     real API.
/// </summary>
/// <remarks>
///     Core is the package a consumer composes against first — the policy, the grants, and the tool
///     builder — so its examples are the ones a coding agent is most likely to copy verbatim. The
///     verification reads the package's shipped XML documentation file rather than the source, so
///     what is proven is exactly what a consumer receives.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Core package compiles.
    /// </summary>
    [Fact]
    public void AgentKitCore_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Core.xml",
            "DemaConsulting.AgentKit.Core",
            "Microsoft.Extensions.AI");
    }
}
