package AgentKitSessions {
    part def RotationEngine {
        doc /* The heart of the system: a deterministic function from a context layout, a compaction level and an injected summarizer to the aged layout. Consolidates everything older than the level-adjusted verbatim tail into one slot appended to tier one - always moving at least the oldest turn - consolidates a full tier as peers into the next tier, rings the coarsest tier, and chunks oversized summarizer input by whole turns so no turn is split across two calls. It measures nothing about the result it built: it reports the level it ran at, the consolidations it performed, and whether a failed consolidation left material unrecorded. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/RotationEngine.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/RotationEngineTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/rotation-engine.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/rotation-engine.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/rotation-engine.yaml */
    }
}