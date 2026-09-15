package AgentKitSessions {
    part def RotationEngine {
        doc /* The heart of the system: a deterministic function from a context layout and an injected summarizer to the next layout. Splits the verbatim history at tier zero budget, folds the overflow into tier one, and cascades where a tier cannot hold its old record and the new material together by degrading the older record one tier coarser. Reports saturation when a consolidation cannot reduce what it was given. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/RotationEngine.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/RotationEngineTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/rotation-engine.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/rotation-engine.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/rotation-engine.yaml */
    }
}