package AgentKitCore {
    part def ProviderSession {
        doc /* The whole interface between the compaction engine and a provider adapter: the seed a fresh session is created from, what one turn produced, the minimal disposable session contract - which also answers how much of its provider's window it occupies and out of how much - and the factory that produces a replacement session at every rotation. The engine never touches a provider API directly. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/provider-session.yaml */
    }
}