package AgentKitTools {
    part def FileCreateDirectoryTool {
        doc /* Publishes the file_create_directory tool, creating a directory the access policy permits the agent to write together with any missing directories above it. Reports an already-present directory as a success in its own words rather than claiming to have created it, and refuses a path already occupied by a file, because a file is never replaced by a directory. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FileCreateDirectoryTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileCreateDirectoryToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-create-directory-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-create-directory-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-create-directory-tool.yaml */
    }
}
