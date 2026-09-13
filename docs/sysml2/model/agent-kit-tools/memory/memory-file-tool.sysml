package AgentKitTools {
    part def MemoryFileTool {
        doc /* Publishes the memory_file tool: embeds the descriptor alone, compares it against the nearest stored memory before storing anything, and either stores the memory and reports its identifier or reports structured non-storage naming the conflicting memory, its details, the similarity and the threshold. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryFileTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryFileToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-file-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-file-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-file-tool.yaml */
    }
}
