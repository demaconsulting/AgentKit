package AgentKitTools {
    part def TextFileWriteTool {
        doc /* Publishes the text_file_write tool, setting a permitted text file's whole content to the supplied text, creating the file when absent and replacing it when present, capturing the file's previous content verbatim into the distinct overwritten buffer slot before replacing it so the displaced content stays recoverable, capturing nothing when the file was absent or empty, refusing a directory, a missing parent directory and a non-text target, and consulting the write decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileWriteTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileWriteToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-write-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-write-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-write-tool.yaml */
    }
}
