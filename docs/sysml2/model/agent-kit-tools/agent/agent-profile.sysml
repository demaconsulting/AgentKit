package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def AgentProfile {
        doc /* One named agent an application is willing to have started on its behalf: the instructions it is given, the names of the tools it may use, an optional narrower set of path grants than its parent holds, and an optional description shown to the model beside the name. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Agent/AgentProfile.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentProfileTests.cs");
            designFiles = ("docs/design/agent-kit-tools/agent/agent-profile.md");
            verificationFiles = ("docs/verification/agent-kit-tools/agent/agent-profile.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/agent/agent-profile.yaml");
        }
    }
}
