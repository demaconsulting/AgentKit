package AgentKitTools {
    part def ImagePack {
        doc /* Publishes the image family as one pack under the image family prefix, requiring the host's vision capability and creating the read tool and the crop tool from the supplied access policy. Both tools are published under every policy, including one that permits no writing: reading an image is a read, and cropping without a destination returns image content rather than writing a file. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImagePack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImagePackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-pack.yaml */
    }
}
