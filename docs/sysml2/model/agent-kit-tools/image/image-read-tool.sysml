package AgentKitTools {
    part def ImageReadTool {
        doc /* Publishes the image_read tool, returning the visual content of one image or PDF file the access policy permits the agent to read, delivered through the guarded construction path so the content is not serialized, and refusing an oversized, unsupported, missing or non-permitted request. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImageReadTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageReadToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-read-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-read-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-read-tool.yaml */
    }
}
