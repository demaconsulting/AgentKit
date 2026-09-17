package AgentKitAgentsCopilot {
    doc
    /*
     * AgentKitAgentsCopilot builds a Microsoft Agent Framework agent from a GitHub Copilot
     * CopilotClient and a supplied tool list, suppressing the runtime's built-in tools by
     * deriving the session allow-list from the same collection it publishes as the session's
     * tools. It also carries an AgentKit Core session over the same runtime, natively: the
     * Copilot SDK exposes no IChatClient, so the compaction engine could not otherwise run on
     * the one provider this project's continuous integration can reach. The session adapter
     * carries a rotation's history on the first message of the session it seeds, because Copilot
     * offers no history channel of any kind and a record charged to the engine's overhead rather
     * than to the conversation would make its accounting drift; takes its occupancy and its window
     * from the runtime's own reporting
     * rather than from any figure the application supplies; and holds the runtime's own
     * compaction threshold clear of the engine's rotation point so two compactors never act on
     * one conversation, ending a session outright when the runtime is left holding a turn the
     * engine's transcript does not. It carries the
     * Microsoft.Agents.AI.GitHub.Copilot dependency, and its RID-specific native runtime, that
     * must be kept out of Core.
     */

    part def AgentKitAgentsCopilotSystem {
        doc /* The AgentKit Copilot Agents system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Copilot.Tests/AgentKitAgentsCopilotTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-copilot.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-copilot.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-copilot.yaml */

        part copilotAgentFactory : CopilotAgentFactory;
        part copilotProviderSession : CopilotProviderSession;
        part copilotProviderSessionFactory : CopilotProviderSessionFactory;
        part copilotSessionObserver : CopilotSessionObserver;
        part copilotSummarizer : CopilotSummarizer;
        part copilotTurnChannel : CopilotTurnChannel;
    }
}
