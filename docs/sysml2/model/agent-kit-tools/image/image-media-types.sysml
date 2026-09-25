package AgentKitTools {
    part def ImageMediaTypes {
        doc /* Maps a file's extension to the media type the image family reads (png, jpg, jpeg, gif, webp and pdf) and to the narrower set a region can be extracted from (png, jpg and jpeg), and composes the refusal for both: stating that an svg is text and naming text_file_read as the reader for that kind of content, stating svgz and unknown types as unsupported with nothing further, and stating of a readable-but-uncroppable type what the format is and which types a region can be taken from, naming no sibling tool. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Image/ImageMediaTypes.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageMediaTypesTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/image/image-media-types.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/image/image-media-types.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/image/image-media-types.yaml */
    }
}
