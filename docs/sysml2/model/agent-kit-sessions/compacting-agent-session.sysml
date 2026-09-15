package AgentKitSessions {
    part def CompactingAgentSession {
        doc /* The session implementation that owns the sequencing: it records each turn in its own transcript, prefers a provider account of the window over its own estimate, and on crossing the rotation threshold consolidates, creates the replacement provider session from the preserved content, and only then disposes the one it replaced. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/CompactingAgentSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/CompactingAgentSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/compacting-agent-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/compacting-agent-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/compacting-agent-session.yaml */
    }
}