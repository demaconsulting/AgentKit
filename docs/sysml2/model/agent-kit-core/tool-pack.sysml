package AgentKitCore {
    part def ToolPack {
        doc /* Defines the contract a package implements to publish its tools as one capability-gated family, together with the set of host capabilities a pack may require. A pack may return the subset of its family the supplied access policy could permit to succeed, so the contract promises a non-null collection of non-null, family-prefixed tools rather than the same tools under every policy. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ToolPack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ToolPackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/tool-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/tool-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/tool-pack.yaml */
    }
}
