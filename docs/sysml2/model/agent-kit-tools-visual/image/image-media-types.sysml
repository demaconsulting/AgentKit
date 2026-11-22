package AgentKitToolsVisual_ImageMediaTypes {
    private import SoftwareItemMetadata::*;

    part def ImageMediaTypes {
        doc /* Maps a file's extension to the media type the image family reads (png, jpg, jpeg, gif, webp and pdf) and to the narrower set a region can be extracted from (png, jpg and jpeg), and composes the refusal for both: stating of an svg read that it is vector content and naming image_view as the tool that draws it, stating of an svg region request that it is vector content and naming no tool, stating svgz and unknown types as unsupported with nothing further, and stating of a readable-but-uncroppable type what the format is and which types a region can be taken from, naming no sibling tool. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Image/ImageMediaTypes.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Image/ImageMediaTypesTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/image/image-media-types.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/image/image-media-types.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/image/image-media-types.yaml");
        }
    }

    // The svg read refusal names the view tool by its published constant, so the two cannot drift.
    dependency ImageMediaTypes to AgentKitToolsVisual_ImageViewTool::ImageViewTool;
}
