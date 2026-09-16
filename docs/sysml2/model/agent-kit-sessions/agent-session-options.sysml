package AgentKitSessions {
    part def AgentSessionOptions {
        doc /* Everything an application configures about one session - instructions, tools, provider window size, compaction policy and summarizer - together with the fixed overhead measured from the instructions and tool declarations, the effective window that leaves, and the conversation size at which the session rotates. Refuses at construction any configuration whose effective window cannot hold the policy tier budgets, which is the bounded-by-construction property made real. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/AgentSessionOptions.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/AgentSessionOptionsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/agent-session-options.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/agent-session-options.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/agent-session-options.yaml */
    }
}