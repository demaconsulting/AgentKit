package AgentKitAgentsCopilot {
    part def CopilotProviderSessionFactory {
        doc /* Creates one seeded, confined Copilot session per rotation: configures the session's system message with the application's instructions alone and composes the seeded history into a separately fenced conversation record for the session's first message to carry, because the Copilot SDK offers no history channel of any kind and the record is untrusted material - it may hold whatever a tool read - that must not travel in the provider's highest-trust channel; fences that record with a marker drawn per record and absent from the material, so the boundary cannot be imitated by what it delimits; reuses CopilotAgentFactory's single confinement path so the allow-list derivation is never duplicated; holds the runtime's own compaction threshold clear of the session engine's rotation point; registers the session observer on the configuration before the session is created so the first turn's events are observed; and releases a session it opened but could not hand to a provider session. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotProviderSessionFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotProviderSessionFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-provider-session-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-provider-session-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-provider-session-factory.yaml */
    }
}
