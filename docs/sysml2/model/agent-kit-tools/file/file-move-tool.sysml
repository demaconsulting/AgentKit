package AgentKitTools_FileMoveTool {
    private import SoftwareItemMetadata::*;

    part def FileMoveTool {
        doc /* Publishes the file_move tool, moving a file from a source the access policy permits the agent to write to a destination it permits the agent to write, removing the source, judging both endpoints by the write decision, and refusing to overwrite an existing destination unless overwrite is explicitly permitted. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileMoveTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileMoveToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-move-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-move-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-move-tool.yaml");
        }
    }
}
