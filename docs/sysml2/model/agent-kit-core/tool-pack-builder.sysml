package AgentKitCore {
    part def ToolPackBuilder {
        doc /* Composes tool packs into the tool list an application offers a model, registering a pack only when the host provides every capability the pack requires and rejecting two packs that claim the same family prefix. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ToolPackBuilder.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ToolPackBuilderTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/tool-pack-builder.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/tool-pack-builder.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/tool-pack-builder.yaml */
    }
}
