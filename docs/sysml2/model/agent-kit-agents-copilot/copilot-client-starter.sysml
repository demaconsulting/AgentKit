package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotClientStarter {
        doc /* Starts a host's Copilot client and, when the start fails, explains the likely cause: an ambient COPILOT_CLI_PATH redirecting in-process hosting to an installation that does not ship the in-process runtime, or a host application that does not reference Microsoft.Copilot.SDK directly so no runtime was provisioned. Keeps the original failure as the cause, passes cancellation through unchanged, and never changes the host's environment or connection. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotClientStarter.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotClientStarterTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-client-starter.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-client-starter.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-client-starter.yaml");
        }
    }
}
