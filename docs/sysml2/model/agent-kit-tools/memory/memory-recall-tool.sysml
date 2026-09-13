package AgentKitTools {
    part def MemoryRecallTool {
        doc /* Publishes the memory_recall tool: embeds the query, searches descriptors alone, and returns up to the author-configured number of matches whole — descriptor, details, provenance and similarity — reporting an empty store as an empty answer rather than a refusal. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryRecallTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryRecallToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-recall-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-recall-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-recall-tool.yaml */
    }
}
