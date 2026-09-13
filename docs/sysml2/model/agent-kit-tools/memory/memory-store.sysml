package AgentKitTools {
    part def MemoryStore {
        doc /* The substitutable persistence contract for memories — add, find, replace, remove, cosine search nearest-first and count — together with the default in-process implementation: a locked list with an exhaustive cosine scan that refuses vectors of mismatched length. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryStore.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryStoreTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-store.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-store.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-store.yaml */
    }
}
