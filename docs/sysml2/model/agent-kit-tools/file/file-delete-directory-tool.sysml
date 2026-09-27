package AgentKitTools {
    part def FileDeleteDirectoryTool {
        doc /* Publishes the file_delete_directory tool, removing a directory and everything beneath it in a location the access policy permits the agent to write. The removal is planned before anything is taken: the walk never follows a link, a link found inside the tree refuses the whole request without removing anything, and a directory that is itself a link is removed as the link alone. A tree holding more entries than the configured ceiling is refused, naming the exact count and the ceiling. The ceiling bounds the damage of a mistake; it is not protection against an agent that intends the destruction. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileDeleteDirectoryTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileDeleteDirectoryToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-delete-directory-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-delete-directory-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-delete-directory-tool.yaml */
    }
}
