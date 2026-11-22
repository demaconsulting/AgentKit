package AgentKitTools_ImageMediaTypes {
    private import SoftwareItemMetadata::*;

    part def ImageMediaTypes {
        doc /* Maps a file's extension to the media type the image family reads (png, jpg, jpeg, gif, webp and pdf) and to the narrower set a region can be extracted from (png, jpg and jpeg), and composes the refusal for both: stating that an svg is text and naming text_file_read as the reader for that kind of content, stating svgz and unknown types as unsupported with nothing further, and stating of a readable-but-uncroppable type what the format is and which types a region can be taken from, naming no sibling tool. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Image/ImageMediaTypes.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Image/ImageMediaTypesTests.cs");
            designFiles = ("docs/design/agent-kit-tools/image/image-media-types.md");
            verificationFiles = ("docs/verification/agent-kit-tools/image/image-media-types.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/image/image-media-types.yaml");
        }
    }
}
