package AgentKitTools {
    part def FileDeleteTool {
        doc /* Publishes the file_delete tool, deleting a single file the access policy permits the agent to write, refusing to delete a directory and never recursing, and reporting a missing file rather than treating it as a silent success. Recovery from a mistaken deletion is source control, not a quarantine. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileDeleteTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileDeleteToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-delete-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-delete-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-delete-tool.yaml */
    }
}
