package AgentKitTools {
    part def Image {
        doc /* The image tool family: policy-governed reading of images and PDF documents, reporting an image's declared pixel dimensions wherever it can establish them, and returning a rectangular region of one — either named in pixels by the caller, or found by trimming the image to the region its own content occupies — inline or written to a destination the policy's write decision permits, published as one pack under the image family prefix and gated on the host's vision capability. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image.yaml */

        part imageMediaTypes : ImageMediaTypes;
        part imageReadTool : ImageReadTool;
        part imageCropTool : ImageCropTool;
        part imageAutoCropTool : ImageAutoCropTool;
        part imagePack : ImagePack;
    }
}
