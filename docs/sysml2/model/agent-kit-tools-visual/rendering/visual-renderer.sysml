package AgentKitToolsVisual_VisualRenderer {
    private import SoftwareItemMetadata::*;
    private import AgentKitToolsVisual_RenderErrorShaper::*;

    part def VisualRenderer {
        doc /* Draws one document or diagram to an opaque white PNG within a pack's bounds - the pixel budget, the largest render side, the smallest preview side for vector pictures, the triangle limit and a timeout that counts the wait for the pack's render gate, abandoning a render a grace period after it as defense in depth while it keeps its gate place until it ends - reporting every note, promoting a picture whose embedded image was refused or that is empty to a failure, describing a diagram's features and colors when asked, and warming up a sample of each format on request. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Rendering/VisualRenderer.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Rendering/VisualRendererTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/rendering/visual-renderer.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/rendering/visual-renderer.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/rendering/visual-renderer.yaml");
        }
    }

    // Every exception a drawing raises is classified, and every note's message cleaned, by the
    // shaper, so a failure has one taxonomy however it arose.
    dependency VisualRenderer to RenderErrorShaper;

    // The renderer's bounds are the pack's options.
    dependency VisualRenderer to AgentKitToolsVisual_VisualOptions::VisualOptions;
}
