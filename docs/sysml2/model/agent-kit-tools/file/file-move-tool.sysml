package AgentKitTools {
    part def FileMoveTool {
        doc /* Publishes the file_move tool, moving a file from a source the access policy permits the agent to write to a destination it permits the agent to write, removing the source, judging both endpoints by the write decision, and refusing to overwrite an existing destination unless overwrite is explicitly permitted. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileMoveTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileMoveToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-move-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-move-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-move-tool.yaml */
    }
}
