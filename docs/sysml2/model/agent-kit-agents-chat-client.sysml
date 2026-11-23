package AgentKitAgentsChatClient {
    private import SoftwareItemMetadata::*;
    public import AgentKitAgentsChatClient_ChatClientAgentFactory::*;
    public import AgentKitAgentsChatClient_ChatClientProviderSession::*;
    public import AgentKitAgentsChatClient_ChatClientProviderSessionFactory::*;
    public import AgentKitAgentsChatClient_PromptSizeRecordingChatClient::*;
    public import AgentKitAgentsChatClient_ChatClientSummarizer::*;
    public import AgentKitAgentsChatClient_ToolPackContextProvider::*;

    doc
    /*
     * AgentKitAgentsChatClient builds a Microsoft Agent Framework agent from any
     * Microsoft.Extensions.AI IChatClient and a supplied tool list. It also carries an
     * AgentKit Core session over that same IChatClient - the one adapter serving the whole
     * stateless provider family - and the summarizer a rotation consolidates through. Core's
     * image-promoting decorator is installed on every agent and every session it builds, so a
     * tool-returned image reaches the model on a provider that would otherwise drop it. It
     * carries the Microsoft.Agents.AI runtime dependency that must be kept out of Core. For a host
     * that builds its own agent or pipeline, it offers a builder extension installing the same
     * decorator and a context provider contributing AgentKit tools per conversation, with each
     * conversation's retained outputs persisted in its session.
     */

    part def AgentKitAgentsChatClientSystem {
        doc /* The AgentKit ChatClient Agents system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/AgentKitAgentsChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client.yaml");
        }

        part chatClientAgentFactory : ChatClientAgentFactory;
        part chatClientProviderSession : ChatClientProviderSession;
        part chatClientProviderSessionFactory : ChatClientProviderSessionFactory;
        part promptSizeRecordingChatClient : PromptSizeRecordingChatClient;
        part chatClientSummarizer : ChatClientSummarizer;
        part toolPackContextProvider : ToolPackContextProvider;
    }
}
