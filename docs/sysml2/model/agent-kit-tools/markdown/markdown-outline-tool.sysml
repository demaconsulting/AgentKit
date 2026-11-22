package AgentKitTools_MarkdownOutlineTool {
    private import SoftwareItemMetadata::*;

    part def MarkdownOutlineTool {
        doc /* Publishes the markdown_outline tool, reporting the heading structure of a Markdown file the access policy permits the agent to read as sections carrying a level, a title and a 1-based line range, computing a section's end as the line before the next heading of the same or higher level, filtering by maxDepth, and skipping a hash inside a fenced or indented code block, so the ranges compose into the text read and cut tools. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Markdown/MarkdownOutlineTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Markdown/MarkdownOutlineToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/markdown/markdown-outline-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/markdown/markdown-outline-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/markdown/markdown-outline-tool.yaml");
        }
    }
}
