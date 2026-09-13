package AgentKitTools {
    part def MemoryForgetTool {
        doc /* Publishes the memory_forget tool: removes exactly one named memory permanently, reports the size of the store afterwards, refuses an identifier the store does not hold, and publishes no way to express a bulk removal. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryForgetTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryForgetToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-forget-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-forget-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-forget-tool.yaml */
    }
}
