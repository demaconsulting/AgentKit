package AgentKitAgentsCopilot_CopilotAgent {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotTurnChannel::*;

    part def CopilotAgent {
        doc /* AgentKit's own Microsoft Agent Framework AIAgent over confined Copilot sessions, because the Copilot SDK ships no Agent Framework agent: creates a runtime session on a conversation's first run and records its identity on the public CopilotAgentSession, resumes that session under an identically confined, freshly built configuration on every later run, and releases it after every run without deleting it, so the conversation stays resumable while no handle outlives a run; deletes the session of a run made without a conversation; streams the root agent's text deltas, tool calls and results, and per-request usage, failing a run the runtime reports a session error for; sends inline data as in-memory attachments; serializes a conversation as its session identity and state bag; and serializes concurrent runs on one conversation so it can never fork. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotAgent.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotAgentSession.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotAgentTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-agent.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-agent.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-agent.yaml");
        }
    }

    // The agent creates, resumes, drives and releases every runtime session through the turn
    // channel seam, because the SDK's session types are sealed and cannot be stood in for.
    dependency CopilotAgent to CopilotTurnChannel;

    // A session error the agent fails a run with quotes the runtime's error redacted with the
    // runtime's sign-in, and a turn's or an open's runtime or SDK failure is relayed as a redacted copy, so no
    // token it knows reaches the exception.
    dependency CopilotAgent to AgentKitAgentsCopilot_CopilotSignIn::CopilotSignIn;
}
