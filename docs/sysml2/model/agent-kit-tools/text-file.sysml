package AgentKitTools {
    part def TextFile {
        doc /* The text file tool family: policy-governed reading, writing and listing of text files, published as one pack under the text_file family prefix. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file.yaml */

        part textFileReadTool : TextFileReadTool;
        part textFileWriteTool : TextFileWriteTool;
        part textFileListTool : TextFileListTool;
        part textFilePack : TextFilePack;
    }
}
