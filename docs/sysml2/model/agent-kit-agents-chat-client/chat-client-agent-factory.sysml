package AgentKitAgentsChatClient {
    part def ChatClientAgentFactory {
        doc /* Builds a Microsoft Agent Framework AIAgent from any IChatClient and a supplied tool list, wrapping the supplied client in AgentKit Core's image-promoting decorator on every agent it builds, with no option to omit it, and rejecting a missing client or a malformed tool list. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientAgentFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientAgentFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client/chat-client-agent-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client/chat-client-agent-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client/chat-client-agent-factory.yaml */
    }
}
