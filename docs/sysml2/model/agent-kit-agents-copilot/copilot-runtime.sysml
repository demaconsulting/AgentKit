package AgentKitAgentsCopilot {
    private import SoftwareItemMetadata::*;

    part def CopilotRuntime {
        doc /* The Copilot runtime AgentKit starts, confines and owns, with the options a host may give it: builds the only Copilot client the package runs on, in Empty mode, spawning the runtime Microsoft.Copilot.SDK provisions into the application's output as a child process over standard input and output whose environment is the host's minus every COPILOT_ and GITHUB_COPILOT_ variable apart from a documented credential, network and telemetry-off pass-through set, on a data directory it creates and marks (reusing a directory it marked earlier and refusing a file or any other non-empty directory before a runtime reads it); exposes only a data directory, a GitHub token, a log level and a logger, leaving every client-level channel of injected capability unset; explains a runtime that cannot start, naming a missing direct SDK reference as the common cause; passes cancellation through unchanged; releases the client when a start fails or is canceled; and disposes it once, after which it cannot be used. Agents, provider-session factories and summarizers are built on it and never dispose it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntime.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntimeOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotRuntimeTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-runtime.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-runtime.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-runtime.yaml");
        }
    }
}