package AgentKitTools {
    part def MarkdownOutlineTool {
        doc /* Publishes the markdown_outline tool, reporting the heading structure of a Markdown file the access policy permits the agent to read as sections carrying a level, a title and a 1-based line range, computing a section's end as the line before the next heading of the same or higher level, filtering by maxDepth, and skipping a hash inside a fenced or indented code block, so the ranges compose into the text read and cut tools. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Markdown/MarkdownOutlineTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Markdown/MarkdownOutlineToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/markdown/markdown-outline-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/markdown/markdown-outline-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/markdown/markdown-outline-tool.yaml */
    }
}
