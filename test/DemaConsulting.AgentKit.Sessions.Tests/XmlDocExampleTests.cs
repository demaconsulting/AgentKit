using DemaConsulting.AgentKit.Tests.Shared;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Tests that every example documented on the AgentKit Sessions public API compiles against the
///     real API.
/// </summary>
/// <remarks>
///     The session package's examples are the instructions a consumer follows to configure tier
///     budgets, supply a summarizer, and run a compacting conversation. An example naming a member
///     the code does not have would fail only once the reader had acted on it, so the examples are
///     compiled rather than reviewed.
/// </remarks>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Tests that every documented example in the Sessions package compiles.
    /// </summary>
    [Fact]
    public void AgentKitSessions_XmlDocExamplesCompile()
    {
        XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.AgentKit.Sessions.xml",
            "DemaConsulting.AgentKit.Sessions",
            "Microsoft.Extensions.AI");
    }
}
