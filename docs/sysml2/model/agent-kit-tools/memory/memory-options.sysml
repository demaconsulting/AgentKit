package AgentKitTools {
    part def MemoryOptions {
        doc /* The author-governed controls over the memory family: the cosine similarity at or above which a new descriptor is treated as a near-duplicate (default 0.88) and the number of memories a recall returns (default 5), validated at construction, immutable, and published with a shared default instance. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Memory/MemoryOptions.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryOptionsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/memory/memory-options.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/memory/memory-options.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/memory/memory-options.yaml */
    }
}
