package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def AgentRunTool {
        doc /* Publishes the agent_run tool, which starts one of the application's registered agents on a task and returns what it said; refuses an absent request, an unknown profile, and a call at the delegation depth ceiling; and composes each child's tools through an internal seam so that no expression at this call site can hand a parent's tool list down. Also defines the immutable, library-authored request bundle the host's runner receives for one delegated agent. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Agent/AgentRunTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentRunToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/agent/agent-run-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/agent/agent-run-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/agent/agent-run-tool.yaml");
        }
    }
}
