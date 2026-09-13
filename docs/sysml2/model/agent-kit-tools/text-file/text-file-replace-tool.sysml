package AgentKitTools {
    part def TextFileReplaceTool {
        doc /* Publishes the text_file_replace tool, replacing one exact, unique occurrence of existing text in a permitted file, refusing a not-found or ambiguous match and saying which case occurred, expressing insertion and deletion through the same substitution, reporting the line span the new text now occupies together with the change in the file's line count, and consulting the write decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileReplaceTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileReplaceToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-replace-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-replace-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-replace-tool.yaml */
    }
}
