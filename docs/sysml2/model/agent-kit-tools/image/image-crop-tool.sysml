package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def ImageCropTool {
        doc /* Publishes the image_crop tool, returning a rectangular region of one image the access policy permits the agent to read, named in pixels from the image's top-left corner and returned inline as image content carrying the source's pixels unaltered, or written as a new PNG file to a destination a read-write grant permits — resolved independently of the grant that admitted the source, never replacing an existing file — refusing rather than reducing a region that is not wholly inside the image and naming the image's real dimensions, and refusing an image whose declared dimensions exceed the host's decode budget before any pixel data is allocated. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Image/ImageCropTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageCropToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/image/image-crop-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/image/image-crop-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/image/image-crop-tool.yaml");
        }
    }
}
