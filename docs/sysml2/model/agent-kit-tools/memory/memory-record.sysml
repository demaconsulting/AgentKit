package AgentKitTools {
    part def MemoryRecord {
        doc /* One memory and one search result: an identifier, the short descriptor that is the only text embedded, the richer unembedded detail payload, optional source document and locator, and the vector the descriptor produced; plus the memory-and-cosine-similarity pair a search returns. Immutable records carrying no link to any other memory. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryRecord.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryRecordTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-record.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-record.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-record.yaml */
    }
}
