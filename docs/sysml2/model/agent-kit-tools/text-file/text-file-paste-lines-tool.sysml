package AgentKitTools {
    part def TextFilePasteLinesTool {
        doc /* Publishes the text_file_paste_lines tool, inserting text previously captured into a named buffer slot into a permitted file at a chosen line or appending it, restoring the captured text exactly so a cut/paste round-trip reproduces the file, reporting the line span the pasted text now occupies and the file's new total line count, refusing a paste from an empty buffer, and consulting the write decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFilePasteLinesTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFilePasteLinesToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-paste-lines-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-paste-lines-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-paste-lines-tool.yaml */
    }
}
