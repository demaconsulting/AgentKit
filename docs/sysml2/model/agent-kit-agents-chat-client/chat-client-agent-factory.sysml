package AgentKitAgentsChatClient_ChatClientAgentFactory {
    private import SoftwareItemMetadata::*;

    part def ChatClientAgentFactory {
        doc /* Builds a Microsoft Agent Framework AIAgent from any IChatClient and a supplied tool list, wrapping the supplied client in AgentKit Core's image-promoting decorator on every agent it builds, with no option to omit it, and rejecting a missing client or a malformed tool list; it also offers a chat client builder extension that installs the same decorator beneath the function-invocation loop of a pipeline a host builds, refusing a pipeline that places it above that loop. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientAgentFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientAgentFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/chat-client-agent-factory.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/chat-client-agent-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/chat-client-agent-factory.yaml");
        }
    }

    // A supplied model selection is applied to every request through Core's settings decorator.
    dependency ChatClientAgentFactory to AgentKitCore::ModelSettingsChatClient;
}
