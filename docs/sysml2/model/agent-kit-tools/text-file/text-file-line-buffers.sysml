package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileLineBuffers {
        doc /* The text-only cut/paste buffer store shared by the cut and paste tools of one composition: named slots that a cut captures into and a paste reads from without consuming, a later capture replacing a slot, thread-safe and living for the lifetime of the tool instances. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileLineBuffers.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileLineBuffersTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-line-buffers.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-line-buffers.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-line-buffers.yaml");
        }
    }
}
