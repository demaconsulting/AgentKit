package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileMoveDirectoryTool {
        doc /* Publishes the file_move_directory tool, moving a directory with everything beneath it between locations the access policy permits the agent to write, judging both endpoints by the write decision. A destination in the same parent renames the directory. Any existing destination is refused outright — there is no overwrite, because replacing a directory would be a recursive destruction wearing a moving verb — as is a destination inside the directory being moved. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileMoveDirectoryTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileMoveDirectoryToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-move-directory-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-move-directory-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-move-directory-tool.yaml");
        }
    }
}
