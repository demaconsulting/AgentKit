package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileWriteTool {
        doc /* Publishes the text_file_write tool, setting a permitted text file's whole content to the supplied text, creating the file when absent and replacing it when present, capturing the file's previous content verbatim into the distinct overwritten buffer slot before replacing it so the displaced content stays recoverable, capturing nothing when the file was absent or empty, refusing a directory, a missing parent directory and a non-text target, and consulting the write decision alone. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileWriteTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileWriteToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-write-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-write-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-write-tool.yaml");
        }
    }
}
