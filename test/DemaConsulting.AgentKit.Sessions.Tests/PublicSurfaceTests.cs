using System.Reflection;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     A mechanical check on the package's public surface, so what an application can depend on is
///     a deliberate list rather than whatever happened to be left public.
/// </summary>
public class PublicSurfaceTests
{
    /// <summary>
    ///     Proves the package exports exactly the types it means to, so a type becoming public is a
    ///     decision someone made rather than an accident.
    /// </summary>
    /// <remarks>
    ///     The list is asserted rather than the count. A count is a metric, and a test that pins one
    ///     pushes whoever comes next toward the number instead of toward the design - which is how a
    ///     genuinely useful type gets hidden to keep a total down. Adding to this list is fine; doing
    ///     it without noticing is not.
    /// </remarks>
    [Fact]
    public void AgentKitSessions_PublicSurface_IsTheDeliberateSet()
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
            "ConsolidationPrompt",
            "ConsolidationRequest",
            "ContextUsage",
            "ContextUsageOrigin",
            "IAgentSession",
            "IProviderSession",
            "IProviderSessionFactory",
            "ISummarizer",
            "InMemoryProviderSession",
            "InMemoryProviderSessionFactory",
            "ProviderSessionSeed",
            "ProviderTurn",
            "TranscriptEntry",
            "TranscriptEntryKind",
        ];

        Assert.Equal(expected, exported);
    }

    /// <summary>
    ///     Proves the internals the design deliberately hides are not part of the public surface, so
    ///     an application cannot take a dependency on the shape of the compaction machinery.
    /// </summary>
    /// <remarks>
    ///     These are hidden because an application has no reason to reach them and every reason not
    ///     to: the layout, the tiers and the transcript are the structure the engine rearranges, and
    ///     pinning their shape in a consumer would make any change to the arrangement a breaking one.
    ///     The saturation family is gone outright, replaced by the reported compaction level.
    /// </remarks>
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
            "TokenEstimator",
        ];

        Assert.All(gone, name => Assert.DoesNotContain(name, exported));
    }
}
