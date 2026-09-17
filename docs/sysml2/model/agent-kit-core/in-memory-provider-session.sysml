package AgentKitCore {
    part def InMemoryProviderSession {
        doc /* A provider session that contacts nothing: it holds its seeded history in memory, answers from a supplied responder, and answers for its own window as every adapter does, so the engine can be exercised end to end without a live model. Its factory records every session it made, which is how rotation creating a replacement and disposing its predecessor is observed. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/InMemoryProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/InMemoryProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/in-memory-provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/in-memory-provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/in-memory-provider-session.yaml */
    }
}