package AgentKitSessions {
    doc
    /*
     * AgentKitSessions is the provider-agnostic session engine: an AgentKit-owned
     * conversation that keeps its own transcript out of session, watches how much of the
     * provider's context window it occupies, and when the window fills consolidates older
     * history into a round-robin structure of tiered slots, creates a fresh provider session
     * seeded with the preserved content, and only then disposes the one it replaced. Rotation
     * rather than in-place reduction is
     * the only mechanism both provider shapes support - one resends history each turn,
     * the other holds it server-side - so the behavior is identical on either. The system
     * carries no provider dependency of its own; provider adapters are a later increment.
     */

    part def AgentKitSessionsSystem {
        doc /* The AgentKit Sessions system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/AgentKitSessionsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions.yaml */

        part agentSession : AgentSession;
        part agentSessionOptions : AgentSessionOptions;
        part compactionPolicy : CompactionPolicy;
        part contextUsage : ContextUsage;
        part tokenEstimator : TokenEstimator;
        part sessionTranscript : SessionTranscript;
        part contextLayout : ContextLayout;
        part rotationEngine : RotationEngine;
        part summarizer : Summarizer;
        part providerSession : ProviderSession;
        part inMemoryProviderSession : InMemoryProviderSession;
        part compactingAgentSession : CompactingAgentSession;
    }
}
