package AgentKitToolsVisual_RenderDestination {
    private import SoftwareItemMetadata::*;

    part def RenderDestination {
        doc /* Admits a destination for a drawn picture only when it names a .png file, the link-following write decision alone permits it, as spelled and where its links lead, it is not a directory and its parent already exists - creating no directory - and writes the picture through a temporary file moved over the target in one step, replacing an earlier picture so the draw, look, fix loop can save to the same name, and leaving no temporary file on failure. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Rendering/RenderDestination.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Rendering/RenderDestinationTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/rendering/render-destination.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/rendering/render-destination.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/rendering/render-destination.yaml");
        }
    }
}
