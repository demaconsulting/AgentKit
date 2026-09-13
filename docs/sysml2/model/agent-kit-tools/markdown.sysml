package AgentKitTools {
    part def Markdown {
        doc /* The markdown tool family: policy-governed reporting of a Markdown file's heading structure as sections with line ranges, published as one pack under the markdown family prefix. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Markdown/MarkdownTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/markdown.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/markdown.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/markdown.yaml */

        part markdownOutlineTool : MarkdownOutlineTool;
        part markdownPack : MarkdownPack;
    }
}
