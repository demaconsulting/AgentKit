using System.Reflection;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     A mechanical check on the package's public surface, so the reduction the redesign is sold on
///     is verified against the built assembly rather than asserted in prose.
/// </summary>
public class PublicSurfaceTests
{
    /// <summary>
    ///     Proves the package exports exactly the eighteen public types the redesign targets, and
    ///     that the types it internalized or deleted are no longer exported.
    /// </summary>
    [Fact]
    public void AgentKitSessions_PublicSurface_IsExactlyEighteenTypes()
    {
        var exported = typeof(CompactingAgentSession).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "AgentSessionCreationException",
            "AgentSessionOptions",
            "AgentSessionResponse",
            "CompactingAgentSession",
            "CompactionLevel",
            "CompactionPolicy",
            "ConsolidationRequest",
            "ContextUsage",
            "ContextUsageOrigin",
            "IAgentSession",
            "IContextUsageReporter",
            "IProviderSession",
            "IProviderSessionFactory",
            "ISummarizer",
            "ProviderSessionSeed",
            "ProviderTurn",
            "TranscriptEntry",
            "TranscriptEntryKind",
        ];

        Assert.Equal(18, exported.Length);
        Assert.Equal(expected, exported);
    }

    /// <summary>
    ///     Proves the deleted and internalized types are not part of the public surface, so the
    ///     saturation family, the tier/layout internals and the shipped test doubles cannot be
    ///     depended upon.
    /// </summary>
    [Fact]
    public void AgentKitSessions_PublicSurface_ExcludesDeletedAndInternalTypes()
    {
        var exported = typeof(CompactingAgentSession).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        string[] gone =
        [
            "SaturationReason",
            "SaturationSignal",
            "ContextLayout",
            "ContextTier",
            "RotationEngine",
            "RotationOutcome",
            "SessionTranscript",
            "ConsolidationPrompt",
            "TokenEstimator",
            "InMemoryProviderSession",
            "InMemoryProviderSessionFactory",
        ];

        Assert.All(gone, name => Assert.DoesNotContain(name, exported));
    }
}
