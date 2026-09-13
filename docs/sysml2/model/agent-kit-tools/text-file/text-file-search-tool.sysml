package AgentKitTools {
    part def TextFileSearchTool {
        doc /* Publishes the text_file_search tool, finding a pattern across the text files the access policy permits the agent to read, reporting matches grep-style as path:line:content, drawing every candidate strictly through the access policy so no file it would refuse is ever surfaced, searching literally by default or as a regular expression, and bounding the result by the policy's ceilings. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileSearchTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileSearchToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-search-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-search-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-search-tool.yaml */
    }
}
