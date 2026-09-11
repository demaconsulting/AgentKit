package AgentKitCore {
    part def ImagePromotingChatClient {
        doc /* Makes an image a tool returned visible to a provider whose tool-result channel cannot carry one, by promoting that image onto a user message inserted after the tool result, leaving a text-only result and the order of every other message unchanged. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ImagePromotingChatClient.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ImagePromotingChatClientTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/image-promoting-chat-client.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/image-promoting-chat-client.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/image-promoting-chat-client.yaml */
    }
}
