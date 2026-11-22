package AgentKitAgentsCopilot_CopilotSessionObserver {
    private import SoftwareItemMetadata::*;

    part def CopilotSessionObserver {
        doc /* Watches one Copilot session's event stream, which is the only place a Copilot turn reveals what it did: holds the latest usage reading as cumulative session state rather than clearing it each turn, collects the current turn's tool calls, results and assistant messages in arrival order while skipping every event a nested agent reports, remembers the runtime's last session error so a silent turn can name its cause, and records whether the runtime compacted, truncated, cleared or rewound the history AgentKit asked it not to. Guarded by one lock because events arrive on a background thread. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSessionObserver.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotSessionObserverTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-session-observer.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-session-observer.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-session-observer.yaml");
        }
    }
}
