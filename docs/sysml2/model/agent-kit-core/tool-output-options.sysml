package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ToolOutputOptions {
        doc /* Carries the preview size and the retention ceilings - one output, everything retained, and the number of outputs - that a composition observes when it retains large tool outputs, with published defaults and validation that a total ceiling can hold one maximum-size output. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputOptionsTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-options.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-options.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-options.yaml");
        }
    }
}
