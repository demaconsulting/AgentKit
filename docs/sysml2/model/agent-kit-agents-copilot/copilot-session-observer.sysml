package AgentKitAgentsCopilot {
    part def CopilotSessionObserver {
        doc /* Watches one Copilot session's event stream, which is the only place a Copilot turn reveals what it did: holds the latest usage reading as cumulative session state rather than clearing it each turn, collects the current turn's tool calls, results and assistant messages in arrival order while skipping a nested agent's own traffic, remembers the runtime's last session error so a silent turn can name its cause, and records whether the runtime compacted or truncated the history AgentKit asked it not to. Guarded by one lock because events arrive on a background thread. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSessionObserver.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotSessionObserverTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-session-observer.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-session-observer.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-session-observer.yaml */
    }
}
