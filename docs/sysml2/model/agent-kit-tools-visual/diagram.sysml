package AgentKitToolsVisual_DiagramSubsystem {
    private import SoftwareItemMetadata::*;
    private import AgentKitToolsVisual_DiagramPackOptions::*;
    private import AgentKitToolsVisual_DiagramHelpTool::*;
    private import AgentKitToolsVisual_DiagramRenderTool::*;
    private import AgentKitToolsVisual_DiagramPack::*;

    part def Diagram {
        doc /* The diagram tool family: drawing Mermaid, PlantUML, SVG, Vega-Lite and OpenSCAD source a model writes inline, under the access policy, and showing a vision-capable model the picture or giving a model that cannot see a description of it in words; reporting a source it cannot draw with its location and the help topic to read; serving the drawing help of the offered languages a topic at a time with the cards written for the application hidden; and saving the picture under the write grant, replacing an earlier one. Published as one pack under the diagram family prefix, whose preview mode follows the host's vision capability. DiagramFormats is a supporting table of the known languages documented in the subsystem design. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Diagram/DiagramTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/diagram.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/diagram.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/diagram.yaml");
        }

        part diagramPackOptions : DiagramPackOptions;
        part diagramHelpTool : DiagramHelpTool;
        part diagramRenderTool : DiagramRenderTool;
        part diagramPack : DiagramPack;
    }
}