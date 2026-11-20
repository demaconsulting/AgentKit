package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotClientStarter {
        doc /* Starts a host's Copilot client and, when the start fails, reports the runtime's error and names COPILOT_CLI_PATH as the cause only when that error names the location the variable points to (an ambient COPILOT_CLI_PATH redirecting in-process hosting to an installation that does not ship the in-process runtime), or a missing direct Microsoft.Copilot.SDK reference, so no runtime was provisioned, when it is unset. Keeps the original failure as the cause, passes cancellation through unchanged, and never changes the host's environment or connection. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotClientStarter.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotClientStarterTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-client-starter.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-client-starter.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-client-starter.yaml");
        }
    }
}
