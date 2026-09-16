package AgentKitSessions {
    part def CompactionPolicy {
        doc /* The one control an application configures about compaction: the maximum number of recent turns kept verbatim, a positive count and a maximum rather than a quota. The counts that give the round-robin structure its shape - slots per tier and tier count - are internal constants. Validated before assignment, with a published default and a shared default instance. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/CompactionPolicy.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/CompactionPolicyTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/compaction-policy.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/compaction-policy.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/compaction-policy.yaml */
    }
}