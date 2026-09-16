package AgentKitSessions {
    part def InMemoryProviderSession {
        doc /* A provider session that contacts nothing: it holds its seeded history in memory, answers from a supplied responder, and can be configured either to report its own context usage or to reveal nothing, so both provider families are reachable without a live model. Its factory records every session it made, which is how rotation creating a replacement and disposing its predecessor is observed. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/InMemoryProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/InMemoryProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/in-memory-provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/in-memory-provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/in-memory-provider-session.yaml */
    }
}