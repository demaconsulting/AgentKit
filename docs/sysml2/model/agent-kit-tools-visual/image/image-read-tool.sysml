package AgentKitToolsVisual_ImageReadTool {
    private import SoftwareItemMetadata::*;

    part def ImageReadTool {
        doc /* Publishes the image_read tool, returning the visual content of one image or PDF file the access policy permits the agent to read, stating that image's declared pixel dimensions in the caption wherever it can establish them and stating none it did not read, delivered through the guarded construction path so the content is not serialized, and refusing an oversized, unsupported, missing or non-permitted request. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Image/ImageReadTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Image/ImageReadToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/image/image-read-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/image/image-read-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/image/image-read-tool.yaml");
        }
    }
}
