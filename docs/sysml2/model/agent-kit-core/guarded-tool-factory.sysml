package AgentKitCore {
    part def GuardedToolFactory {
        doc /* The only supported way to construct a tool, validating the tool name and applying a selective result-delivery guard so that text and content reach the runtime in the form the tool produced them while any other result is serialized to JSON as the underlying function factory would have done. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/GuardedToolFactory.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/GuardedToolFactoryTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/guarded-tool-factory.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/guarded-tool-factory.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/guarded-tool-factory.yaml */
    }
}
