package AgentKitAgentsCopilot_CopilotSummarizer {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotRuntime::*;

    part def CopilotSummarizer {
        doc /* Consolidates session history on a short-lived, tool-free Copilot session of its own, using the consolidation prompt AgentKit Core publishes, so a consolidation never spends the context of the session it is compacting and never counts toward the occupancy that triggered the rotation. The session carries no tools and an empty allow-list, holds the runtime's own compaction threshold clear of the session engine's rotation point, and is released on every path including failure; an empty answer is returned as an empty record rather than raised. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSummarizer.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotSummarizerTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-summarizer.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-summarizer.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-summarizer.yaml");
        }
    }

    // The summarizer takes the runtime AgentKit owns, never a host's client, and opens its sessions on
    // that runtime's client; it never disposes the runtime.
    dependency CopilotSummarizer to CopilotRuntime;

    // Each consolidation runs on a validated model selection when the host supplies one.
    dependency CopilotSummarizer to AgentKitCore_ModelSettings::ModelSettings;
}
