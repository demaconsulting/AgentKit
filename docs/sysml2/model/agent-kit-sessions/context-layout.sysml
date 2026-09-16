package AgentKitSessions {
    part def ContextLayout {
        doc /* The whole of a session context as this library accounts for it - fixed overhead, the coarse tiers holding consolidated records, and the verbatim recent history - together with the construction bound it can never exceed after a rotation and the most-stable-first seed a fresh provider session is created from. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/ContextLayout.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/ContextLayoutTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/context-layout.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/context-layout.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/context-layout.yaml */
    }
}