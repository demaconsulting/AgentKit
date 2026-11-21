package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def AgentKitChatReducer {
        doc /* A chat reducer for a host that owns its chat history: when the estimated history size exceeds a threshold it consolidates every message older than a verbatim tail of whole turns into one record through the injected summarizer and the consolidation prompt, keeping system messages and never splitting a tool call from its result; a reduced-fidelity alternative to the session engine with one record, one level, no rotation and character estimates. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/AgentKitChatReducer.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/AgentKitChatReducerTests.cs");
            designFiles = ("docs/design/agent-kit-core/agent-kit-chat-reducer.md");
            verificationFiles = ("docs/verification/agent-kit-core/agent-kit-chat-reducer.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/agent-kit-chat-reducer.yaml");
        }
    }
}
