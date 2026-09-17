package AgentKitAgentsCopilot {
    part def CopilotProviderSessionFactory {
        doc /* Creates one seeded, confined Copilot session per rotation: renders the seeded history as a fenced conversation record appended to the session's system message after the instructions, because the Copilot SDK offers no history channel of any kind; reuses CopilotAgentFactory's single confinement path so the allow-list derivation is never duplicated; disables the runtime's own infinite-session compaction; registers the session observer on the configuration before the session is created so the first turn's events are observed; and releases a session it opened but could not hand to a provider session. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotProviderSessionFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotProviderSessionFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-provider-session-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-provider-session-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-provider-session-factory.yaml */
    }
}
