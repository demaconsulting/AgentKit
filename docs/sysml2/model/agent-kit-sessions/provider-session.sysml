package AgentKitSessions {
    part def ProviderSession {
        doc /* The whole interface between the compaction engine and a provider adapter: the seed a fresh session is created from, what one turn produced, the minimal disposable session contract, and the factory that produces a replacement session at every rotation. The engine never touches a provider API directly. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/ProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/ProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/provider-session.yaml */
    }
}