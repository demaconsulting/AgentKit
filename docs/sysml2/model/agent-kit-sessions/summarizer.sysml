package AgentKitSessions {
    part def Summarizer {
        doc /* The injected out-of-session consolidation contract, the request that expresses the ratchet by carrying the previous record in as an input, and the documented default consolidation prompt that asks for specificity and content and never for a target size. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/Summarizer.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/SummarizerTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/summarizer.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/summarizer.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/summarizer.yaml */
    }
}