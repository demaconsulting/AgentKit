package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MemoryFileTool {
        doc /* Publishes the memory_file tool: embeds the descriptor alone, compares it against the nearest stored memory before storing anything, and either stores the memory and reports its identifier or reports structured non-storage naming the conflicting memory, its details, the similarity and the threshold. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryFileTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryFileToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-file-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-file-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-file-tool.yaml");
        }
    }
}
