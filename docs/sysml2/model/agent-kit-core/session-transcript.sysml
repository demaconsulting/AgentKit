package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def SessionTranscript {
        doc /* The append-only verbatim history the engine keeps out of session, grouped into whole turns, and its indivisible entries. A turn groups the message, the answer and all intervening tool traffic, so the boundary split that retains the newest turns and returns everything older is turn-granular and a tool call is never separated from its result. Append-only between rotations is what preserves a provider prompt cache. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/SessionTranscript.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/SessionTranscriptTests.cs");
            designFiles = ("docs/design/agent-kit-core/session-transcript.md");
            verificationFiles = ("docs/verification/agent-kit-core/session-transcript.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/session-transcript.yaml");
        }
    }
}