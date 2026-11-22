package AgentKitTools_AgentPack {
    private import SoftwareItemMetadata::*;

    part def AgentPack {
        doc /* Publishes the agent family as one pack under the agent family prefix, requires the host's delegation capability, holds the profiles and the runner the application registered, validates at the application's own composition that no profile widens the parent's policy, carries into each child only the profiles the child's own narrowed policy still covers, and composes each child from a fresh creation of the registered child packs against the child's own policy, composing a child that holds tools with retention of large tool outputs when the application asks. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Agent/AgentPack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentPackTests.cs");
            designFiles = ("docs/design/agent-kit-tools/agent/agent-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools/agent/agent-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/agent/agent-pack.yaml");
        }
    }
}
