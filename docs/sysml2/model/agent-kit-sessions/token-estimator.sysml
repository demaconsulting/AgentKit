package AgentKitSessions {
    part def TokenEstimator {
        doc /* The deterministic character-ratio arithmetic the fallback estimate rests on: the tokens a run of text occupies, the tokens one transcript entry occupies including its framing, and the fixed overhead a set of tool declarations imposes on every turn. Used when a provider reports no usage and when the session sizes a seed it has not yet sent. Deterministic by design, so the rotation engine can be unit-tested without a model. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/TokenEstimator.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/TokenEstimatorTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/token-estimator.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/token-estimator.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/token-estimator.yaml */
    }
}