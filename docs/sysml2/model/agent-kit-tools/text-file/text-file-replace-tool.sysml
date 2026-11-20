package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileReplaceTool {
        doc /* Publishes the text_file_replace tool, replacing one exact, unique occurrence of existing text in a permitted file, refusing a not-found or ambiguous match and saying which case occurred, expressing insertion and deletion through the same substitution, reporting the line span the new text now occupies together with the change in the file's line count, and consulting the write decision alone. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileReplaceTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileReplaceToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-replace-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-replace-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-replace-tool.yaml");
        }
    }
}
