package AgentKitTools {
    part def MemoryReviseTool {
        doc /* Publishes the memory_revise tool: replaces a memory's descriptor, details and provenance together and recomputes its vector, taking provenance as a stated parameter and clearing rather than inheriting what the caller did not state, so no memory silently keeps a superseded citation. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryReviseTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryReviseToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-revise-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-revise-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-revise-tool.yaml */
    }
}
