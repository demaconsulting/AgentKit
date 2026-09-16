package AgentKitSessions {
    part def CompactionPolicy {
        doc /* The validated controls governing compaction: the per-tier token budgets that bound the context, the fraction of the effective window at which a session rotates, and the output-to-input ratio at or above which a consolidation is reported as saturated. Optional constructor parameters over published defaults, validated before assignment, with a shared default instance. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/CompactionPolicy.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/CompactionPolicyTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/compaction-policy.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/compaction-policy.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/compaction-policy.yaml */
    }
}