package AgentKitTools {
    part def FileMoveDirectoryTool {
        doc /* Publishes the file_move_directory tool, moving a directory with everything beneath it between locations the access policy permits the agent to write, judging both endpoints by the write decision. A destination in the same parent renames the directory. Any existing destination is refused outright — there is no overwrite, because replacing a directory would be a recursive destruction wearing a moving verb — as is a destination inside the directory being moved. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileMoveDirectoryTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileMoveDirectoryToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-move-directory-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-move-directory-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-move-directory-tool.yaml */
    }
}
