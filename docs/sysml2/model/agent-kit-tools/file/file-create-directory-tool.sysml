package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileCreateDirectoryTool {
        doc /* Publishes the file_create_directory tool, creating a directory the access policy permits the agent to write together with any missing directories above it, provided every one of those is itself in a permitted location; a request whose missing parents would reach above every grant is refused with nothing created at any level. Reports an already-present directory as a success in its own words rather than claiming to have created it, and refuses a path already occupied by a file, because a file is never replaced by a directory. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileCreateDirectoryTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileCreateDirectoryToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-create-directory-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-create-directory-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-create-directory-tool.yaml");
        }
    }
}
