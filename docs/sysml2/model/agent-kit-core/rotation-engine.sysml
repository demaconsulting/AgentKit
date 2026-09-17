package AgentKitCore {
    part def RotationEngine {
        doc /* The heart of the session engine: a deterministic function from a context layout, a compaction level and an injected summarizer to the aged layout. Consolidates everything older than the level-adjusted verbatim tail into one slot appended to tier one - always moving at least the oldest turn - consolidates a full tier as peers into the next tier, and rings the coarsest tier. It measures nothing about the result it built: it reports the consolidations it performed and whether a failed consolidation left material unrecorded. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/RotationEngine.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/RotationEngineTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/rotation-engine.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/rotation-engine.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/rotation-engine.yaml */
    }
}