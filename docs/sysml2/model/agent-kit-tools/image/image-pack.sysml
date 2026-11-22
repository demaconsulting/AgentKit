package AgentKitTools_ImagePack {
    private import SoftwareItemMetadata::*;

    part def ImagePack {
        doc /* Publishes the image family as one pack under the image family prefix, requiring the host's vision capability and creating the read tool, the crop tool and the auto-crop tool from the supplied access policy. All three tools are published under every policy, including one that permits no writing: reading an image is a read, and taking a region — whether the caller names it or the tool finds it — returns image content rather than writing a file unless a destination is named. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Image/ImagePack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Image/ImagePackTests.cs");
            designFiles = ("docs/design/agent-kit-tools/image/image-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools/image/image-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/image/image-pack.yaml");
        }
    }
}
