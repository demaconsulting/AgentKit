package AgentKitToolsVisual_ImageViewTool {
    private import SoftwareItemMetadata::*;

    part def ImageViewTool {
        doc /* Publishes the image_view tool, drawing one document, drawing or diagram file the access policy permits the agent to read - SVG, PDF, PPTX, VSDX, Mermaid, PlantUML, Vega-Lite, OpenSCAD or a raster image - to an opaque PNG returned as image content with a caption naming the format, the page of the page count, the size and every note: it judges the input by the read decision before learning anything about it, reads it once within the binary ceiling and draws those bytes with the file's own folder as the base of its references, reports a file it cannot draw as shaped text naming no help topic, and refuses a malformed request, an unsupported type or an oversized file or picture. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Image/ImageViewTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Image/ImageViewToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/image/image-view-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/image/image-view-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/image/image-view-tool.yaml");
        }
    }

    // Every view draws through the pack's renderer, so the pack's bounds and render gate apply.
    dependency ImageViewTool to AgentKitToolsVisual_VisualRenderer::VisualRenderer;

    // Every file a viewed document references is confined by the mapped file-access policy.
    dependency ImageViewTool to AgentKitToolsVisual_VisualAccess::VisualAccess;

    // A file the library cannot draw is reported as text the shaper composes.
    dependency ImageViewTool to AgentKitToolsVisual_RenderErrorShaper::RenderErrorShaper;

    // A file the tool cannot draw is refused through the family's one media-type map.
    dependency ImageViewTool to AgentKitToolsVisual_ImageMediaTypes::ImageMediaTypes;
}
