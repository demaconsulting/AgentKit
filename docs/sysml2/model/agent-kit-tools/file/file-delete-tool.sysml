package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileDeleteTool {
        doc /* Publishes the file_delete tool, deleting a single file the access policy permits the agent to write, refusing to delete a directory and never recursing, and reporting a missing file rather than treating it as a silent success. Recovery from a mistaken deletion is source control, not a quarantine. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileDeleteTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileDeleteToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-delete-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-delete-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-delete-tool.yaml");
        }
    }
}
