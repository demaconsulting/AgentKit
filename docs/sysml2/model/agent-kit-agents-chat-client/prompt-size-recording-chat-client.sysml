package AgentKitAgentsChatClient {
    part def PromptSizeRecordingChatClient {
        doc /* Records the prompt size of each individual request a turn makes and keeps the most recent, so a session reads occupancy as the last request's prompt rather than as the usage a tool-calling loop sums across every request it made. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.ChatClient/PromptSizeRecordingChatClient.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/PromptSizeRecordingChatClientTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-chat-client/prompt-size-recording-chat-client.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-chat-client/prompt-size-recording-chat-client.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-chat-client/prompt-size-recording-chat-client.yaml */
    }
}
