package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def File {
        doc /* The type-agnostic file tool family: policy-governed listing, copying, moving and deletion of files of any type, and creation, moving and recursive deletion of the directories that hold them, published as one pack under the file family prefix. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/File/FileTests.cs");
            designFiles = ("docs/design/agent-kit-tools/file.md");
            verificationFiles = ("docs/verification/agent-kit-tools/file.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/file.yaml");
        }

        part fileListTool : FileListTool;
        part fileCopyTool : FileCopyTool;
        part fileMoveTool : FileMoveTool;
        part fileDeleteTool : FileDeleteTool;
        part fileCreateDirectoryTool : FileCreateDirectoryTool;
        part fileMoveDirectoryTool : FileMoveDirectoryTool;
        part fileDeleteDirectoryTool : FileDeleteDirectoryTool;
        part filePack : FilePack;
    }
}
