package AgentKitAgentsChatClient_ChatClientProviderSession {
    private import SoftwareItemMetadata::*;

    part def ChatClientProviderSession {
        doc /* Carries one AgentKit Core session over any Microsoft.Extensions.AI IChatClient, seeding a message list from the instructions, tools and history a rotation supplies, resending the accumulated conversation on every turn, recording tool calls and results as paired transcript entries, and reporting the provider's own input-token count against a window supplied at construction - zero until the first turn, and refused outright where the provider reports no usage. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientProviderSession.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientProviderSessionTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/chat-client-provider-session.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/chat-client-provider-session.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/chat-client-provider-session.yaml");
        }
    }
}
