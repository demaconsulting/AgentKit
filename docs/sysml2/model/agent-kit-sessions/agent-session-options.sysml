package AgentKitSessions {
    part def AgentSessionOptions {
        doc /* Everything an application configures about one session - instructions, tools, compaction policy and summarizer - together with the fixed overhead measured from the instructions and tool declarations. It carries no provider window: the window is a fact about the provider, stated where the provider is constructed and answered by the adapter thereafter. Validates the application's own numbers - a required summarizer and no null tool declaration - and makes no prediction, in a currency it cannot measure, about whether the arrangement will settle. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Sessions/AgentSessionOptions.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Sessions.Tests/AgentSessionOptionsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-sessions/agent-session-options.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-sessions/agent-session-options.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-sessions/agent-session-options.yaml */
    }
}