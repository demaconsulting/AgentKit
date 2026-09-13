package AgentKitAgentsCopilot {
    part def CopilotAgentFactory {
        doc /* Builds a Microsoft Agent Framework AIAgent from a GitHub Copilot CopilotClient and a supplied tool list, deriving the session available-tools allow-list from the same collection it publishes as the session's tools so the two cannot drift apart, installing a default-safe permission handler a host may override, letting the host name the model backing the session while deferring to the runtime's default when it names none, and taking no ownership of the client. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotAgentFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotAgentFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-agent-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-agent-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-agent-factory.yaml */
    }
}
