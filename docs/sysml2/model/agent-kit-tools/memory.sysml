package AgentKitTools {
    part def Memory {
        doc /* The memory tool family: a searchable record of what an agent has learned, each memory a short embedded descriptor with a richer unembedded detail payload and its provenance, filed with deterministic near-duplicate detection, recalled whole by vector search, corrected with or without re-embedding, and forgotten one at a time. Published as one pack under the memory family prefix, requiring no host capability, consulting no path policy, and taking its embedding generator, its controls and its persistence from the composing application. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory.yaml */

        part memoryOptions : MemoryOptions;
        part memoryRecord : MemoryRecord;
        part memoryStore : MemoryStore;
        part memoryFileTool : MemoryFileTool;
        part memoryRecallTool : MemoryRecallTool;
        part memoryUpdateTool : MemoryUpdateTool;
        part memoryReviseTool : MemoryReviseTool;
        part memoryForgetTool : MemoryForgetTool;
        part memoryPack : MemoryPack;
    }
}
