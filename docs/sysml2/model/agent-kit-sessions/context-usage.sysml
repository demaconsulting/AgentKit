package AgentKitSessions {
    part def ContextUsage {
        doc /* The one shape both provider families are reduced to - tokens occupied, window size, the tokens the conversation occupies, and whether the figures were reported by the provider or estimated by this library - together with the optional reporting contract a provider session implements when it can account for its own window and omits when it cannot. Carrying the conversation is what keeps every rotation comparison within one currency, because the split is made by whoever produced the totals. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/ContextUsage.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/ContextUsageTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/context-usage.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/context-usage.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/context-usage.yaml */
    }
}