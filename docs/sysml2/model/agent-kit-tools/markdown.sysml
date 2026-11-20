package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def Markdown {
        doc /* The markdown tool family: policy-governed reporting of a Markdown file's heading structure as sections with line ranges, published as one pack under the markdown family prefix. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Markdown/MarkdownTests.cs");
            designFiles = ("docs/design/agent-kit-tools/markdown.md");
            verificationFiles = ("docs/verification/agent-kit-tools/markdown.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/markdown.yaml");
        }

        part markdownOutlineTool : MarkdownOutlineTool;
        part markdownPack : MarkdownPack;
    }
}
