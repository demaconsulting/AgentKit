package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotAgent::*;
    private import AgentKitAgentsCopilot_CopilotAgentFactory::*;
    private import AgentKitAgentsCopilot_CopilotCli::*;
    private import AgentKitAgentsCopilot_CopilotProviderSession::*;
    private import AgentKitAgentsCopilot_CopilotProviderSessionFactory::*;
    private import AgentKitAgentsCopilot_CopilotRuntime::*;
    private import AgentKitAgentsCopilot_CopilotSessionObserver::*;
    private import AgentKitAgentsCopilot_CopilotSummarizer::*;
    private import AgentKitAgentsCopilot_CopilotTurnChannel::*;

    doc
    /*
     * AgentKitAgentsCopilot builds AgentKit's own Microsoft Agent Framework agent on a Copilot
     * runtime it starts and owns (the installed Copilot CLI by default, version-checked, or the SDK's
     * bundled runtime on request; Empty mode, the host's COPILOT_ and GITHUB_COPILOT_ variables
     * withheld apart from an allow-listed credential, network and telemetry-off set, a data directory it owns) and a
     * supplied tool list, suppressing the runtime's built-in tools by
     * deriving a source-qualified session allow-list from the same collection it publishes as the
     * session's tools, and running a conversation that streams, resumes across runs and agent
     * instances, and is confined identically on every run. It also carries an AgentKit Core
     * session over the same runtime, natively: the Copilot SDK exposes no IChatClient, so the
     * compaction engine could not otherwise run on the one provider this project's continuous
     * integration can reach. The session adapter
     * carries a rotation's history on the first message of the session it seeds, because Copilot
     * offers no history channel of any kind and a record charged to the engine's overhead rather
     * than to the conversation would make its accounting drift; takes its occupancy and its window
     * from the runtime's own reporting
     * rather than from any figure the application supplies; and holds the runtime's own
     * compaction threshold clear of the engine's rotation point so two compactors never act on
     * one conversation, ending a session outright when the runtime is left holding a turn the
     * engine's transcript does not. It carries the Microsoft.Copilot.SDK and
     * Microsoft.Agents.AI.Abstractions dependencies that must be kept out of Core; the SDK's
     * RID-specific native runtime is provisioned only by a host application that opts into the bundled
     * runtime, never by this library.
     */

    part def AgentKitAgentsCopilotSystem {
        doc /* The AgentKit Copilot Agents system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/AgentKitAgentsCopilotTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot.yaml");
        }

        part copilotAgent : CopilotAgent;
        part copilotAgentFactory : CopilotAgentFactory;
        part copilotCli : CopilotCli;
        part copilotProviderSession : CopilotProviderSession;
        part copilotProviderSessionFactory : CopilotProviderSessionFactory;
        part copilotRuntime : CopilotRuntime;
        part copilotSessionObserver : CopilotSessionObserver;
        part copilotSummarizer : CopilotSummarizer;
        part copilotTurnChannel : CopilotTurnChannel;
    }
}
