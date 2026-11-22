package AgentKitToolsVisual_ImageSubsystem {
    private import SoftwareItemMetadata::*;
    private import AgentKitToolsVisual_ImageMediaTypes::*;
    private import AgentKitToolsVisual_ImageReadTool::*;
    private import AgentKitToolsVisual_ImageCropTool::*;
    private import AgentKitToolsVisual_ImageAutoCropTool::*;
    private import AgentKitToolsVisual_ImagePack::*;

    part def Image {
        doc /* The image tool family: policy-governed reading of images and PDF documents, reporting an image's declared pixel dimensions wherever it can establish them, and returning a rectangular region of one — either named in pixels by the caller, or found by trimming the image to the region its own content occupies — inline or written to a destination the policy's write decision permits, published as one pack under the image family prefix and gated on the host's vision capability. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Image/ImageTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/image.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/image.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/image.yaml");
        }

        part imageMediaTypes : ImageMediaTypes;
        part imageReadTool : ImageReadTool;
        part imageCropTool : ImageCropTool;
        part imageAutoCropTool : ImageAutoCropTool;
        part imagePack : ImagePack;
    }
}
