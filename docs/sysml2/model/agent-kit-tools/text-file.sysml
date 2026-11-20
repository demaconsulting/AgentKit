package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFile {
        doc /* The text file tool family: policy-governed searching, reading and editing of the contents of text files, published as one pack under the text_file family prefix. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file.yaml");
        }

        part textFileSearchTool : TextFileSearchTool;
        part textFileReadTool : TextFileReadTool;
        part textFileCreateTool : TextFileCreateTool;
        part textFileWriteTool : TextFileWriteTool;
        part textFileReplaceTool : TextFileReplaceTool;
        part textFileCutLinesTool : TextFileCutLinesTool;
        part textFileCopyLinesTool : TextFileCopyLinesTool;
        part textFilePasteLinesTool : TextFilePasteLinesTool;
        part textFileLineBuffers : TextFileLineBuffers;
        part textFilePack : TextFilePack;
    }
}
