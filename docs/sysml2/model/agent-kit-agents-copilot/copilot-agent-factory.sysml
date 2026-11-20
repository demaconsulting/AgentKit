package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotAgentFactory {
        doc /* Builds AgentKit's own Microsoft Agent Framework agent from a Copilot client and a supplied tool list, and is the single place every Copilot session the package builds is confined: derives a source-qualified (custom:) available-tools allow-list from the same collection it publishes as the session's tool definitions so the two cannot drift apart, refuses a tool named like a runtime built-in before any session exists, closes every channel of runtime-injected capability including large-output diversion and the built-in GitHub MCP server, pins the permission mode so every request reaches the handler, installs a default-deny permission handler a host may replace, confines a resumed agent session identically to a created one, carries the host's model, working directory and instructions, and takes no ownership of the client. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotAgentFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotAgentFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-agent-factory.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-agent-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-agent-factory.yaml");
        }
    }

    // The factory constructs the agent and hands it the per-run configuration builders and the
    // channel openers over the host's client; it never reaches the agent's runtime sessions itself.
    dependency CopilotAgentFactory to CopilotAgent;

    // Create hands the agent the production channels over the host's client: the keeping opener
    // and the resumer for a conversation, and the deleting opener for a run made without one.
    dependency CopilotAgentFactory to CopilotTurnChannel;
}
