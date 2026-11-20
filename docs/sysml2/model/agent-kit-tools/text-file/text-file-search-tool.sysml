package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TextFileSearchTool {
        doc /* Publishes the text_file_search tool, finding a pattern across the text files the access policy permits the agent to read, reporting matches grep-style as path:line:content, drawing every candidate strictly through the access policy so no file it would refuse is ever surfaced, searching literally by default or as a regular expression, and bounding the result by the policy's ceilings. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/TextFile/TextFileSearchTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileSearchToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/text-file/text-file-search-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/text-file/text-file-search-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/text-file/text-file-search-tool.yaml");
        }
    }
}
