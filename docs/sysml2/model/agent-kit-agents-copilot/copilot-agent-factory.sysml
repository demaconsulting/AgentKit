package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotAgentFactory {
        doc /* Builds AgentKit's own Microsoft Agent Framework agent on a Copilot runtime the package owns and a supplied tool list, and is the single place every Copilot session the package builds is confined: derives a source-qualified (custom:) available-tools allow-list from the same collection it publishes as the session's tool definitions so the two cannot drift apart, refuses a tool named like a runtime built-in before any session exists, withholds every channel of runtime-injected capability a session's options reach, including large-output diversion, the host environment context in the system prompt, experimental features, embedding retrieval and the built-in GitHub MCP server, which it disables by name, so the confinement does not rest on the runtime's mode alone, pins the permission mode so every request reaches the handler, installs a default-deny permission handler a host may replace, confines a resumed agent session identically to a created one, carries the host's model, working directory and instructions, and takes no ownership of the runtime. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotAgentFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotAgentFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-agent-factory.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-agent-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-agent-factory.yaml");
        }
    }

    // The factory constructs the agent and hands it the per-run configuration builders and the
    // channel openers over the runtime's client; it never reaches the agent's runtime sessions itself.
    dependency CopilotAgentFactory to CopilotAgent;

    // Create hands the agent the production channels over the runtime's client: the keeping opener
    // and the resumer for a conversation, and the deleting opener for a run made without one.
    dependency CopilotAgentFactory to CopilotTurnChannel;

    // Create takes the runtime AgentKit owns, never a host's client, and reads its client for the
    // channel openers; it never disposes the runtime.
    dependency CopilotAgentFactory to CopilotRuntime;
}
