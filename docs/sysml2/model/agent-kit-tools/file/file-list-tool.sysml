package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def FileListTool {
        doc /* Publishes the file_list tool, listing the files of any type the access policy permits the agent to read, grouped under an absolute location header with names relative to it, mirroring the caller's dialect, reporting on discovery every permitted location including an empty one, and enumerating strictly through the access policy so no escaped file is ever listed. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/File/FileListTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileListToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file/file-list-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file/file-list-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file/file-list-tool.yaml");
        }
    }
}
