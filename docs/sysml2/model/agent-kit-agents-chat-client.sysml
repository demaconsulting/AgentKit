package AgentKitAgentsChatClient {
    doc
    /*
     * AgentKitAgentsChatClient builds a Microsoft Agent Framework agent from any
     * Microsoft.Extensions.AI IChatClient and a supplied tool list, installing AgentKit
     * Core's image-promoting decorator on every agent it builds so a tool-returned image
     * reaches the model on a provider that would otherwise drop it. It also carries an
     * AgentKit Core session over that same IChatClient - the one adapter serving the whole
     * stateless provider family - and the summarizer a rotation consolidates through. It
     * carries the Microsoft.Agents.AI runtime dependency that must be kept out of Core.
     */

    part def AgentKitAgentsChatClientSystem {
        doc /* The AgentKit ChatClient Agents system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/AgentKitAgentsChatClientTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client.yaml */

        part chatClientAgentFactory : ChatClientAgentFactory;
        part chatClientProviderSession : ChatClientProviderSession;
        part chatClientProviderSessionFactory : ChatClientProviderSessionFactory;
        part promptSizeRecordingChatClient : PromptSizeRecordingChatClient;
        part chatClientSummarizer : ChatClientSummarizer;
    }
}
