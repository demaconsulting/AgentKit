package AgentKitToolsVisual_RenderErrorShaper {
    private import SoftwareItemMetadata::*;

    part def RenderErrorShaper {
        doc /* Turns a failed render into text a model can act on: classifies it by the types in its chain of exceptions and never by message text, locates it from the typed exception or the uniform location prefix, quotes the offending source line with a caret, gives advice written for the model rather than for a host or command line, lists every note, names a help topic only when the help tool serves it, and removes the working directory, the grant roots and the input folder from the text. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools.Visual/Rendering/RenderErrorShaper.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Visual.Tests/Rendering/RenderErrorShaperTests.cs");
            designFiles = ("docs/design/agent-kit-tools-visual/rendering/render-error-shaper.md");
            verificationFiles = ("docs/verification/agent-kit-tools-visual/rendering/render-error-shaper.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools-visual/rendering/render-error-shaper.yaml");
        }
    }
}
