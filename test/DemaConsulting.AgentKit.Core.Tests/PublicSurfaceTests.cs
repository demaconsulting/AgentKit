namespace DemaConsulting.AgentKit.Core.Tests;

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
    public void AgentKitCore_PublicSurface_IsTheDeliberateSet()
    {
        // Arrange / Act: every type an application outside this assembly can name
        var exported = typeof(CompactingAgentSession).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Assert: the tool surface and the session surface, which now ship as one package
        string[] expected =
        [
            "AccessLevel",
            "AgentSessionOptions",
            "AgentSessionResponse",
            "CompactingAgentSession",
            "CompactionLevel",
            "ConsolidationPrompt",
            "ConsolidationRequest",
            "ContextUsage",
            "DenialReason",
            "GuardedToolFactory",
            "HostCapabilities",
            "IAgentSession",
            "IProviderSession",
            "IProviderSessionFactory",
            "ISummarizer",
            "IToolPack",
            "ImagePromotingChatClient",
            "InMemoryProviderSession",
            "InMemoryProviderSessionFactory",
            "PathPolicy",
            "PathRule",
            "ProviderSessionSeed",
            "ProviderTurn",
            "RealPathResolver",
            "ToolLimits",
            "ToolName",
            "ToolPackBuilder",
            "ToolResult",
            "TranscriptEntry",
            "TranscriptEntryKind",
        ];

        Assert.Equal(expected, exported);
    }

    /// <summary>
    ///     Proves the internals the design deliberately hides, and the types the pruning removed, are
    ///     not part of the public surface.
    /// </summary>
    /// <remarks>
    ///     The layout, the tiers and the transcript are hidden because an application has no reason
    ///     to reach them and every reason not to: they are the structure the engine rearranges, and
    ///     pinning their shape in a consumer would make any change to the arrangement a breaking one.
    ///     The rest are gone outright — the saturation family, replaced by the reported compaction
    ///     level; the token estimator and the usage origin that went with it, now that every provider
    ///     session answers for its own window; the compaction policy, folded into the session options
    ///     as one number; and the creation exception, which existed to hand back a provider session
    ///     whose disposal had just failed.
    /// </remarks>
    [Fact]
    public void AgentKitCore_PublicSurface_ExcludesDeletedAndInternalTypes()
    {
        // Arrange / Act: the exported names, as a set
        var exported = typeof(CompactingAgentSession).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Assert: nothing hidden or deleted has crept back out
        string[] gone =
        [
            "AgentSessionCreationException",
            "CompactionPolicy",
            "ContextLayout",
            "ContextTier",
            "ContextUsageOrigin",
            "IContextUsageReporter",
            "RotationEngine",
            "RotationOutcome",
            "SaturationReason",
            "SaturationSignal",
            "SessionTranscript",
            "SessionTurn",
            "Slot",
            "Tier",
            "TokenEstimator",
        ];

        Assert.All(gone, name => Assert.DoesNotContain(name, exported));
    }
}
