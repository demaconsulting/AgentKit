package AgentKitAgentsChatClient_ChatClientProviderSessionFactory {
    private import SoftwareItemMetadata::*;

    part def ChatClientProviderSessionFactory {
        doc /* Creates ChatClientProviderSession instances from a seed - once at the start of a conversation and again at every rotation - holding the chat client and the context window the application stated once where it configured its provider, and never disposing that client. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientProviderSessionFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientProviderSessionFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/chat-client-provider-session-factory.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/chat-client-provider-session-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/chat-client-provider-session-factory.yaml");
        }
    }
}
