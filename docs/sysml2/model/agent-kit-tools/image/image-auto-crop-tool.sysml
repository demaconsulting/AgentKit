package AgentKitTools {
    part def ImageAutoCropTool {
        doc /* Publishes the image_auto_crop tool, trimming one image the access policy permits the agent to read down to the region its content occupies — determining what counts as background from the image's own border rather than assuming a color, tolerating pixels within a small fixed tolerance of it so anti-aliasing and compression artifacts do not defeat the trim, expanding the result by a padding the caller may state and clamping that padding to the image's edges rather than refusing it — returned inline as image content carrying the source's pixels unaltered, or written as a new PNG file to a destination a read-write grant permits, resolved independently of the grant that admitted the source and never replacing an existing file; refusing an image that is entirely background without writing anything, and refusing an image whose declared dimensions exceed the host's decode budget before any pixel data is allocated. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImageAutoCropTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageAutoCropToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-auto-crop-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-auto-crop-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-auto-crop-tool.yaml */
    }
}
