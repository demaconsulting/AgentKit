package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ToolResult {
        doc /* Constructs the results a guarded tool returns to the model - text, structured data, binary content, a caption accompanied by an image, a refusal, or the declaration that an output is too large to return - and also defines the DenialReason enumeration naming why a tool refused an operation. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolResult.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolResultTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-result.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-result.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-result.yaml");
        }
    }
}
