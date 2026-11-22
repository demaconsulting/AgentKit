package AgentKitToolsVisual_DiagramPack {
    private import SoftwareItemMetadata::*;

    part def DiagramPack {
        doc /* Publishes the diagram family as one pack under the diagram family prefix, in a preview mode (DiagramPreview) chosen at construction or from the host's capabilities: image preview requires the vision capability and returns pictures, text preview requires nothing and returns descriptions. It creates the help tool and the render tool from the supplied access policy, withholding the render tool in text preview where nothing may be written, holds its options, its filtered help catalog and the render gate every drawing of the pack shares, offers an instruction paragraph it never injects, and offers an optional warm-up. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Diagram/DiagramPack.cs", "src/DemaConsulting.AgentKit.Tools.Visual/Diagram/DiagramPreview.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Diagram/DiagramPackTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/diagram/diagram-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/diagram/diagram-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/diagram/diagram-pack.yaml");
        }
    }

    // The pack is configured by the host's diagram options and checks their one cross-value rule.
    dependency DiagramPack to AgentKitToolsVisual_DiagramPackOptions::DiagramPackOptions;

    // The pack creates the one renderer, and so the one render gate, its render tool draws through.
    dependency DiagramPack to AgentKitToolsVisual_VisualRenderer::VisualRenderer;

    // The pack builds the filtered help catalog both of its tools share.
    dependency DiagramPack to AgentKitToolsVisual_DiagramHelpTool::DiagramHelpTool;

    // The pack creates the render tool in its preview mode.
    dependency DiagramPack to AgentKitToolsVisual_DiagramRenderTool::DiagramRenderTool;
}