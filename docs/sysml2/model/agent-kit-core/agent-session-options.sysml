package AgentKitCore {
    part def AgentSessionOptions {
        doc /* Everything an application configures about one session - instructions, tools, summarizer and the maximum number of most-recent turns kept verbatim. It carries no provider window: the window is a fact about the provider, stated where the provider is constructed and answered by the adapter thereafter. Validates the application's own numbers - a required summarizer, no null tool declaration and a positive verbatim tail - and makes no prediction, in a currency it cannot measure, about whether the arrangement will settle. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/AgentSessionOptions.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/AgentSessionOptionsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/agent-session-options.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/agent-session-options.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/agent-session-options.yaml */
    }
}