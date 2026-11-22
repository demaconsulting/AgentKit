package AgentKitAgentsChatClient_ChatClientSummarizer {
    private import SoftwareItemMetadata::*;

    part def ChatClientSummarizer {
        doc /* Consolidates session history through an IChatClient of the application's choosing, out of the session being compacted, sending the consolidation prompt AgentKit Core publishes as a single user message with no tools and no history, and returning an empty answer as an empty record rather than as a failure. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientSummarizer.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientSummarizerTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/chat-client-summarizer.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/chat-client-summarizer.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/chat-client-summarizer.yaml");
        }
    }
}
