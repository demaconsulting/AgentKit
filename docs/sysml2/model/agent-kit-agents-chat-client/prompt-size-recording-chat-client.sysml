package AgentKitAgentsChatClient_PromptSizeRecordingChatClient {
    private import SoftwareItemMetadata::*;

    part def PromptSizeRecordingChatClient {
        doc /* Records the prompt size of each individual request a turn makes and keeps the most recent, so a session reads occupancy as the last request's prompt rather than as the usage a tool-calling loop sums across every request it made. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/PromptSizeRecordingChatClient.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/PromptSizeRecordingChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/prompt-size-recording-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/prompt-size-recording-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/prompt-size-recording-chat-client.yaml");
        }
    }
}
