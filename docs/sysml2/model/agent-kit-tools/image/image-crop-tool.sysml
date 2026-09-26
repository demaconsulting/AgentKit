package AgentKitTools {
    part def ImageCropTool {
        doc /* Publishes the image_crop tool, returning a rectangular region of one image the access policy permits the agent to read, named in pixels from the image's top-left corner and returned inline as image content carrying the source's pixels unaltered, or written as a new PNG file to a destination a read-write grant permits — resolved independently of the grant that admitted the source, never replacing an existing file — refusing rather than reducing a region that is not wholly inside the image and naming the image's real dimensions, and refusing an image whose declared dimensions exceed the host's decode budget before any pixel data is allocated. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImageCropTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageCropToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-crop-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-crop-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-crop-tool.yaml */
    }
}
