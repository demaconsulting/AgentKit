package AgentKitCore {
    part def ContextLayout {
        doc /* The whole of a session context as this library accounts for it as a round-robin structure - a fixed number of tiers each a ring of a fixed number of consolidated slots, and the verbatim recent turns - together with the most-stable-first seed a fresh provider session is created from. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ContextLayout.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ContextLayoutTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/context-layout.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/context-layout.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/context-layout.yaml */
    }
}