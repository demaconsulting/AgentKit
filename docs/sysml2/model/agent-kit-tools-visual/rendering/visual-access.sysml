package AgentKitToolsVisual_VisualAccess {
    private import SoftwareItemMetadata::*;

    part def VisualAccess {
        doc /* Maps the access policy onto the file-access policy the rendering library consults for every file a drawn document references - the grant roots for the written and real path, no links below the starting folder, and the policy's own read decision with its deny patterns - created per render and never the library's allow-everything policy, refusing with fixed text that names no host path, and chooses the working directory as the base of inline source only when the agent may read it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Rendering/VisualAccess.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Rendering/VisualAccessTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/rendering/visual-access.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/rendering/visual-access.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/rendering/visual-access.yaml");
        }
    }
}
