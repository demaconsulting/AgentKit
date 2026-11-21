package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ToolOutputReadTool {
        doc /* Publishes the tool_output_read tool, which pages through a retained tool output by line number under a range header in the shape text_file_read uses, taking an output identifier and a line range and no path, and refusing a page over the result ceiling. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputReadTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputReadToolTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-read-tool.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-read-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-read-tool.yaml");
        }
    }
}
