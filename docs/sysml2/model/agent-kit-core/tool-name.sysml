package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ToolName {
        doc /* Defines the family-prefixed tool naming convention and validates names against it, rejecting names that would collide with the bare file access tool names published by the Agent Framework. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolName.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolNameTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-name.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-name.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-name.yaml");
        }
    }
}
