package AgentKitCore {
    part def AgentSession {
        doc /* The session contract an application programs against - send a message, read what the window looks like, dispose - together with what one turn reports back about the answer, the resulting usage, whether the session rotated, the compaction level it is at and whether the turn had to drop history outright. Deliberately small and provider-silent, so an application wanting a long-running agent need not know how compaction is achieved. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/AgentSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/AgentSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/agent-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/agent-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/agent-session.yaml */
    }
}