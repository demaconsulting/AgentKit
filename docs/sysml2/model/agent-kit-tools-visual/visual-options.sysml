package AgentKitToolsVisual_VisualOptions {
    private import SoftwareItemMetadata::*;
    private import ScalarValues::*;

    part def VisualOptions {
        doc /* Carries the rendering bounds a host gives a visual tool pack beyond the access policy's limits - the render timeout including the wait to start, the largest render side, the smallest preview side a small vector picture is drawn at, the triangle limit of a three-dimensional model and the number of renders a pack runs at once - refusing every value outside its documented range at the point it is set, so every accepted option is applied as stated. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/VisualOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/VisualOptionsTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/visual-options.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/visual-options.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/visual-options.yaml");
        }

        // The C# property is a TimeSpan; modelled here in seconds.
        attribute renderTimeout : Real;
        attribute maxRenderSide : Integer;
        attribute minPreviewSide : Integer;
        attribute scadMaxTriangles : Integer;
        attribute maxConcurrentRenders : Integer;
    }
}
