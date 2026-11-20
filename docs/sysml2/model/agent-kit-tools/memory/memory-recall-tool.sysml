package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MemoryRecallTool {
        doc /* Publishes the memory_recall tool: embeds the query, searches descriptors alone, and returns up to the author-configured number of matches whole — descriptor, details, provenance and similarity — reporting an empty store as an empty answer rather than a refusal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryRecallTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryRecallToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-recall-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-recall-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-recall-tool.yaml");
        }
    }
}
