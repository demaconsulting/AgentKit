package AgentKitAgentsCopilot {
    doc
    /*
     * AgentKitAgentsCopilot builds a Microsoft Agent Framework agent from a GitHub Copilot
     * CopilotClient and a supplied tool list, suppressing the runtime's built-in tools by
     * deriving the session allow-list from the same collection it publishes as the session's
     * tools. It carries the Microsoft.Agents.AI.GitHub.Copilot dependency, and its
     * RID-specific native runtime, that must be kept out of Core.
     */

    part def AgentKitAgentsCopilotSystem {
        doc /* The AgentKit Copilot Agents system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/AgentKitAgentsCopilotTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot.yaml */

        part copilotAgentFactory : CopilotAgentFactory;
    }
}
