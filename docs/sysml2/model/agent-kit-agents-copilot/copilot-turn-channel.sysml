package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotTurnChannel {
        doc /* The internal seam over the Copilot SDK's sealed session types: one interface reducing a runtime session to its identity, taking a turn with any attachments, and releasing it; the delegates that create and resume one; and the production implementation that owns exactly one runtime session handle, deletes the session on release for the engine and the summarizer but keeps it for an agent's conversation, never throws on release, and disposes the client never. It exists because CopilotSession and CopilotClient are sealed with non-public constructors and no virtual members, so nothing above them could be exercised without a live runtime and credentials; it also places no timeout of its own on a turn, leaving the caller's cancellation token as the only deadline. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotTurnChannel.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/FakeCopilotTurnChannel.cs", "test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotTurnChannelTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-turn-channel.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-turn-channel.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-turn-channel.yaml");
        }
    }
}
