package AgentKitSessions {
    part def RotationEngine {
        doc /* The heart of the system: a deterministic function from a context layout and an injected summarizer to the next layout that fits. Consolidates everything older than the level-adjusted verbatim tail into one slot appended to tier one, consolidates a full tier as peers into the next tier and rings the coarsest tier, chunks oversized summarizer input, and escalates the compaction level then drops the oldest slot or verbatim turn until the built seed fits - reporting when material was dropped. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/RotationEngine.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/RotationEngineTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/rotation-engine.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/rotation-engine.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/rotation-engine.yaml */
    }
}