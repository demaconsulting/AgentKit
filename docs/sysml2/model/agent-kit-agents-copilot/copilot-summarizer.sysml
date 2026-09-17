package AgentKitAgentsCopilot {
    part def CopilotSummarizer {
        doc /* Consolidates session history on a short-lived, tool-free Copilot session of its own, using the consolidation prompt AgentKit Core publishes, so a consolidation never spends the context of the session it is compacting and never counts toward the occupancy that triggered the rotation. The session carries no tools and an empty allow-list, has the runtime's own compaction disabled, and is released on every path including failure; an empty answer is returned as an empty record rather than raised. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSummarizer.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotSummarizerTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot/copilot-summarizer.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot/copilot-summarizer.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot/copilot-summarizer.yaml */
    }
}
