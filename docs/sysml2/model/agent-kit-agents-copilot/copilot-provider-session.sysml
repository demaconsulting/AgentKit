package AgentKitAgentsCopilot {
    part def CopilotProviderSession {
        doc /* Carries one AgentKit Core session over one GitHub Copilot session: sends each message as a single turn, records the turn's tool calls and results as pairs carrying the runtime's own call identifiers, reports the occupancy, the limit and the conversation split exactly as the runtime reported them rather than from any figure the application supplied, refuses a turn the runtime answered without reporting usage or whose history the runtime rewrote behind AgentKit's back, and owns and releases its runtime session without ever disposing the client. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotProviderSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotProviderSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-provider-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-provider-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-provider-session.yaml */
    }
}
