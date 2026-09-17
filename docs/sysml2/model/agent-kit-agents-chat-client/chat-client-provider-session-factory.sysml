package AgentKitAgentsChatClient {
    part def ChatClientProviderSessionFactory {
        doc /* Creates ChatClientProviderSession instances from a seed - once at the start of a conversation and again at every rotation - holding the chat client and the context window the application stated once where it configured its provider, and never disposing that client. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientProviderSessionFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientProviderSessionFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client/chat-client-provider-session-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client/chat-client-provider-session-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client/chat-client-provider-session-factory.yaml */
    }
}
