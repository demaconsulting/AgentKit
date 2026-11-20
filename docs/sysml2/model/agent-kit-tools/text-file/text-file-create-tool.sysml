package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileCreateTool {
        doc /* Publishes the text_file_create tool, writing a new text file the access policy permits the agent to write, refusing to replace an existing file with a plain statement that names no other tool, refusing a missing parent directory, reporting the created file's character and total line counts, and consulting the write decision alone. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileCreateTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileCreateToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-create-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-create-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-create-tool.yaml");
        }
    }
}
