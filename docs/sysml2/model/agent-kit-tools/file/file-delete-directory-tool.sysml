package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileDeleteDirectoryTool {
        doc /* Publishes the file_delete_directory tool, removing a directory and everything beneath it in a location the access policy permits the agent to write. The removal is planned before anything is taken: the walk never follows a link, a link found inside the tree refuses the whole request without removing anything, and a directory that is itself a link is removed as the link alone. A tree holding more entries than the configured ceiling is refused, naming the exact count and the ceiling. The ceiling bounds the damage of a mistake; it is not protection against an agent that intends the destruction. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileDeleteDirectoryTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileDeleteDirectoryToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-delete-directory-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-delete-directory-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-delete-directory-tool.yaml");
        }
    }
}
