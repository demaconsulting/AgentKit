package AgentKitCore_ToolOutputSearchTool {
    private import SoftwareItemMetadata::*;

    part def ToolOutputSearchTool {
        doc /* Publishes the tool_output_search tool, which finds a literal or regular-expression pattern in a retained tool output and reports matching lines grep-style by number, taking an output identifier and search arguments and no path, with a pattern of at most 1,000 characters, a one-second match timeout and a result bounded by the result ceiling. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputSearchTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputSearchToolTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-search-tool.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-search-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-search-tool.yaml");
        }
    }
}
