package AgentKitTools {
    part def TextFileListTool {
        doc /* Publishes the text_file_list tool, listing the permitted files grouped under an absolute location header with names given relative to it, mirroring the caller's path dialect, enumerating strictly through the access policy so no escaped file is ever listed. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileListTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileListToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-list-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-list-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-list-tool.yaml */
    }
}
