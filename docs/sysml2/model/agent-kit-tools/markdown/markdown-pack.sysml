package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MarkdownPack {
        doc /* Publishes the markdown tool family as one pack: the single outline tool, governed by the supplied policy, requiring no host capability. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Markdown/MarkdownPack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Markdown/MarkdownPackTests.cs");
            designFiles = ("docs/design/agent-kit-tools/markdown/markdown-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools/markdown/markdown-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/markdown/markdown-pack.yaml");
        }
    }
}
