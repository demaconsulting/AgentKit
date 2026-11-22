package AgentKitAgentsCopilot_CopilotProviderSessionFactory {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotRuntime::*;

    part def CopilotProviderSessionFactory {
        doc /* Creates one seeded, confined Copilot session per rotation: configures the session's system message with the application's instructions alone and composes the seeded history into a labeled conversation record for the session's first message to carry, because the Copilot SDK offers no history channel of any kind and a record charged to the engine's overhead rather than to the conversation would make the engine's picture of its own window drift at every rotation; opens and closes that record with fixed lines marking where the handover ends and the current message begins; reuses CopilotAgentFactory's single confinement path so the allow-list derivation is never duplicated; holds the runtime's own compaction threshold clear of the session engine's rotation point; registers the session observer on the configuration before the session is created so the first turn's events are observed; and releases a session it opened but could not hand to a provider session. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotProviderSessionFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotProviderSessionFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-provider-session-factory.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-provider-session-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-provider-session-factory.yaml");
        }
    }

    // The factory takes the runtime AgentKit owns, never a host's client, and opens its sessions on
    // that runtime's client; it never disposes the runtime.
    dependency CopilotProviderSessionFactory to CopilotRuntime;

    // Every session is configured from a validated model selection when the host supplies one, and
    // its window becomes the ceiling the session accounts against.
    dependency CopilotProviderSessionFactory to AgentKitCore::ModelSettings;
}
