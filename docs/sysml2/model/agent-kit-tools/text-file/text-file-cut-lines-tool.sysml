package AgentKitTools {
    part def TextFileCutLinesTool {
        doc /* Publishes the text_file_cut_lines tool, removing a range of lines from a permitted file and capturing them into a named buffer slot first so every deletion is recoverable, reporting the count, the first and last captured line, the file's new total line count and which line now sits where the removal began, and consulting the write decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileCutLinesTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileCutLinesToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-cut-lines-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-cut-lines-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-cut-lines-tool.yaml */
    }
}
