package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileCopyTool {
        doc /* Publishes the file_copy tool, copying a file the access policy permits the agent to read to a destination it permits the agent to write, judging the source by the read decision and the destination by the write decision, and refusing to overwrite an existing destination unless overwrite is explicitly permitted. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileCopyTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileCopyToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-copy-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-copy-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-copy-tool.yaml");
        }
    }
}
