package AgentKitTools {
    part def TextFileCopyLinesTool {
        doc /* Publishes the text_file_copy_lines tool, capturing a range of lines from a permitted file into a named buffer slot while leaving the source byte-identical, the non-destructive sibling of text_file_cut_lines, reporting the count and the first and last captured line, and consulting the read decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileCopyLinesTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileCopyLinesToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-copy-lines-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-copy-lines-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-copy-lines-tool.yaml */
    }
}
