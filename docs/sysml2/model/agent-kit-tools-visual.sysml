package AgentKitToolsVisual {
    private import SoftwareItemMetadata::*;
    private import AgentKitToolsVisual_ImageSubsystem::*;
    private import AgentKitToolsVisual_RenderingSubsystem::*;
    private import AgentKitToolsVisual_VisualOptions::*;

    doc
    /*
     * AgentKitToolsVisual is an optional capability package of guarded visual tool families,
     * composed through the AgentKitCore contract. It is a peer of the other capability
     * packages an application may attach, depending on AgentKitCore alone and never on
     * AgentKitTools, and it carries the imaging library its families need so that
     * AgentKitTools carries none. It declares the visual tool families the package ships.
     */

    part def AgentKitToolsVisualSystem {
        doc /* The AgentKit Tools Visual system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/AgentKitToolsVisualTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual.yaml");
        }

        part image : Image;
        part renderingSubsystem : Rendering;
        part visualOptions : VisualOptions;
    }
}
