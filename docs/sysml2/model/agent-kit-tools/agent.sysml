package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def Agent {
        doc /* The agent tool family: application-authored delegation from one agent to another, published as one pack under the agent family prefix, gated on the host's delegation capability, and bounded by the agent depth ceiling so that a child's tools are always composed from the child's own state rather than filtered from the parent's. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentTests.cs");
            designFiles = ("docs/design/agent-kit-tools/agent.md");
            verificationFiles = ("docs/verification/agent-kit-tools/agent.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/agent.yaml");
        }

        part agentProfile : AgentProfile;
        part agentRunTool : AgentRunTool;
        part agentPack : AgentPack;
    }
}
