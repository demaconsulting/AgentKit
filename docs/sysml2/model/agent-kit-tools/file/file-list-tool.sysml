package AgentKitTools {
    part def FileListTool {
        doc /* Publishes the file_list tool, listing the files of any type the access policy permits the agent to read, grouped under an absolute location header with names relative to it, mirroring the caller's dialect, reporting on discovery every permitted location including an empty one, and enumerating strictly through the access policy so no escaped file is ever listed. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileListTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileListToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-list-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-list-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-list-tool.yaml */
    }
}
