package AgentKitToolsVisual_RenderingSubsystem {
    private import SoftwareItemMetadata::*;
    public import AgentKitToolsVisual_VisualAccess::*;
    public import AgentKitToolsVisual_VisualRenderer::*;
    public import AgentKitToolsVisual_RenderErrorShaper::*;
    public import AgentKitToolsVisual_RenderDestination::*;

    part def Rendering {
        doc /* The internal pieces every visual tool that draws shares: confining every file a drawn document references to what the access policy permits the agent to read, drawing within the pack's pixel, size, triangle, time and concurrency bounds on an opaque white background, promoting a refused or empty picture to a failure, shaping every failure into text a model can act on with no host path, and saving a picture only where the write decision permits, replacing an earlier one in one step. RenderRequest, RenderOutcome and RenderFailure are supporting value types documented in the subsystem design. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Rendering/RenderingTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/rendering.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/rendering.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/rendering.yaml");
        }

        part visualAccess : VisualAccess;
        part visualRenderer : VisualRenderer;
        part renderErrorShaper : RenderErrorShaper;
        part renderDestination : RenderDestination;
    }
}
