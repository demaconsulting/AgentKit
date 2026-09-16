package AgentKitSessions {
    part def SessionTranscript {
        doc /* The append-only verbatim history the engine keeps out of session, its indivisible entries, and the tier-zero boundary split that retains the newest turns within a budget and snaps past any leading tool result so a tool call is never separated from its result. Append-only between rotations is what preserves a provider prompt cache. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/SessionTranscript.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/SessionTranscriptTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/session-transcript.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/session-transcript.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/session-transcript.yaml */
    }
}