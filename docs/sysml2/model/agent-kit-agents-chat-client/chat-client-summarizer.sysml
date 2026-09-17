package AgentKitAgentsChatClient {
    part def ChatClientSummarizer {
        doc /* Consolidates session history through an IChatClient of the application's choosing, out of the session being compacted, sending the consolidation prompt AgentKit Core publishes as a single user message with no tools and no history, and returning an empty answer as an empty record rather than as a failure. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.ChatClient/ChatClientSummarizer.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ChatClientSummarizerTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client/chat-client-summarizer.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client/chat-client-summarizer.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client/chat-client-summarizer.yaml */
    }
}
