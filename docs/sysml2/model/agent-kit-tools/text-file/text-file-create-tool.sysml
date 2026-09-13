package AgentKitTools {
    part def TextFileCreateTool {
        doc /* Publishes the text_file_create tool, writing a new text file the access policy permits the agent to write, refusing to replace an existing file with a plain statement that names no other tool, refusing a missing parent directory, and consulting the write decision alone. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileCreateTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileCreateToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-create-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-create-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-create-tool.yaml */
    }
}
