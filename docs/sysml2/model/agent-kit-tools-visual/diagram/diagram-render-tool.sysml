package AgentKitToolsVisual_DiagramRenderTool {
    private import SoftwareItemMetadata::*;

    part def DiagramRenderTool {
        doc /* Publishes the diagram_render tool, drawing Mermaid, PlantUML, SVG, Vega-Lite or OpenSCAD source given inline: in image preview it returns an opaque PNG with a caption naming the language, the size, the saved file and every note; in text preview it requires save_as and returns a structured description of the saved picture - its size, notes, features and dominant colors. It refuses a language not offered, a missing source, a width below 1, a source beyond the read ceiling and an unusable destination before drawing, resolves inline references against the working directory only when it may be read, reports a source it cannot draw as shaped text naming the diagnostics help topic, refuses a picture beyond the binary ceiling, and saves under the write grant, replacing an earlier picture. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Diagram/DiagramRenderTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Diagram/DiagramRenderToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/diagram/diagram-render-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/diagram/diagram-render-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/diagram/diagram-render-tool.yaml");
        }
    }

    // Every drawing goes through the pack's renderer, so the pack's bounds and render gate apply.
    dependency DiagramRenderTool to AgentKitToolsVisual_VisualRenderer::VisualRenderer;

    // Every file inline source references is confined by the mapped file-access policy.
    dependency DiagramRenderTool to AgentKitToolsVisual_VisualAccess::VisualAccess;

    // A source the library cannot draw is reported as text the shaper composes, naming a help topic.
    dependency DiagramRenderTool to AgentKitToolsVisual_RenderErrorShaper::RenderErrorShaper;

    // A picture is saved only where the write decision permits, replacing an earlier one atomically.
    dependency DiagramRenderTool to AgentKitToolsVisual_RenderDestination::RenderDestination;

    // A shaped failure names only a help topic the help tool serves.
    dependency DiagramRenderTool to AgentKitToolsVisual_DiagramHelpTool::DiagramHelpTool;
}