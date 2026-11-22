package AgentKitToolsVisual_DiagramHelpTool {
    private import SoftwareItemMetadata::*;

    part def DiagramHelpTool {
        doc /* Publishes the diagram_help tool, serving the rendering library's drawing help filtered to the offered languages with the cards written for the application and for formats the family does not draw from source hidden: with no topic, one line per top-level topic and its summary; with a topic id or alias, the topic's card followed by the full text of its example sources; with a topic it does not serve, a refusal suggesting served topics that contain it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Diagram/DiagramHelpTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Diagram/DiagramHelpToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/diagram/diagram-help-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/diagram/diagram-help-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/diagram/diagram-help-tool.yaml");
        }
    }
}