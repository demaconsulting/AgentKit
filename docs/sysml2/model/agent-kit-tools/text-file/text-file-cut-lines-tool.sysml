package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileCutLinesTool {
        doc /* Publishes the text_file_cut_lines tool, removing a range of lines from a permitted file and capturing them into a named buffer slot first so every deletion is recoverable, reporting the count, the first and last captured line, the file's new total line count and which line now sits where the removal began, and consulting the write decision alone. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileCutLinesTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileCutLinesToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-cut-lines-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-cut-lines-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-cut-lines-tool.yaml");
        }
    }
}
