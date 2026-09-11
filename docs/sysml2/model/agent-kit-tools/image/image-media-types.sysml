package AgentKitTools {
    part def ImageMediaTypes {
        doc /* Maps a file's extension to the media type the image family reads (png, jpg, jpeg, gif, webp and pdf) and composes the refusal for an unsupported type, redirecting an svg to text_file_read and refusing svgz and unknown types without a redirect. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImageMediaTypes.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageMediaTypesTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-media-types.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-media-types.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-media-types.yaml */
    }
}
