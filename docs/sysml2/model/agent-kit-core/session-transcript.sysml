package AgentKitCore {
    part def SessionTranscript {
        doc /* The append-only verbatim history the engine keeps out of session, grouped into whole turns, and its indivisible entries. A turn groups the message, the answer and all intervening tool traffic, so the boundary split that retains the newest turns and returns everything older is turn-granular and a tool call is never separated from its result. Append-only between rotations is what preserves a provider prompt cache. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/SessionTranscript.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/SessionTranscriptTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/session-transcript.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/session-transcript.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/session-transcript.yaml */
    }
}