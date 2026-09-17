package AgentKitAgentsCopilot {
    part def CopilotTurnChannel {
        doc /* The internal seam over the Copilot SDK's sealed session types: one interface reducing a runtime session to taking a turn and releasing it, the delegate that opens one, and the production implementation that owns exactly one runtime session and disposes the client never. It exists because CopilotSession and CopilotClient are sealed with non-public constructors and no virtual members, so nothing above them could be exercised without a live runtime and credentials; it also places no timeout of its own on a turn, leaving the caller's cancellation token as the only deadline. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotTurnChannel.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/FakeCopilotTurnChannel.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-turn-channel.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-turn-channel.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-turn-channel.yaml */
    }
}
