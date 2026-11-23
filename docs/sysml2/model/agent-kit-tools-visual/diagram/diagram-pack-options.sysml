package AgentKitToolsVisual_DiagramPackOptions {
    private import SoftwareItemMetadata::*;
    private import ScalarValues::*;

    part def DiagramPackOptions {
        doc /* Carries the options a diagram pack is given - the rendering bounds its drawings observe and the diagram languages it offers - refusing an absent bound and a language list that is empty or names a language that is unknown, null or listed twice where it is set, storing each name in canonical form in a private copy exposed read-only, so every accepted option is applied as stated. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Diagram/DiagramPackOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Diagram/DiagramPackOptionsTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/diagram/diagram-pack-options.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/diagram/diagram-pack-options.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/diagram/diagram-pack-options.yaml");
        }

        // The C# property is a read-only list of canonical language names.
        attribute formats : String [1..*];
    }

    // The options carry the rendering bounds of the pack's drawings.
    dependency DiagramPackOptions to AgentKitToolsVisual_VisualOptions::VisualOptions;
}