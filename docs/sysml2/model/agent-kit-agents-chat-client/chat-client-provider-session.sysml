package AgentKitAgentsChatClient {
    part def ChatClientProviderSession {
        doc /* Carries one AgentKit Core session over any Microsoft.Extensions.AI IChatClient, seeding a message list from the instructions, tools and history a rotation supplies, resending the accumulated conversation on every turn, recording tool calls and results as paired transcript entries, and reporting the provider's own input-token count against a window supplied at construction - zero until the first turn, and refused outright where the provider reports no usage. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client/chat-client-provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client/chat-client-provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client/chat-client-provider-session.yaml */
    }
}
