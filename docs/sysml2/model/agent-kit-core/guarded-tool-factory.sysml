package AgentKitCore {
    part def GuardedToolFactory {
        doc /* The only supported way to construct a tool, validating the tool name and applying a result-delivery guard so a tool's result reaches the runtime in the form the tool produced it rather than as serialized JSON. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/GuardedToolFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/GuardedToolFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/guarded-tool-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/guarded-tool-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/guarded-tool-factory.yaml */
    }
}
