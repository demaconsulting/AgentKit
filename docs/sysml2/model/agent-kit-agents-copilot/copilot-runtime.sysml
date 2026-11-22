package AgentKitAgentsCopilot_CopilotRuntime {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotCli::*;

    part def CopilotRuntime {
        doc /* The Copilot runtime AgentKit starts, confines and owns, with the options a host may give it: builds the only Copilot client the package runs on, in Empty mode, spawning as a child process over standard input and output either the installed Copilot CLI (the default: the CLI the host names or the one discovery finds, refused before any client exists when it is missing or older than the supported minimum) or, only when the host chooses it, the runtime Microsoft.Copilot.SDK provisions into the application's output, never an executable the host's environment names, with the same mode, environment and data directory for either; the environment is the host's minus every COPILOT_ and GITHUB_COPILOT_ variable apart from a documented credential, network and telemetry-off pass-through set, on a data directory it creates and marks (reusing a directory carrying exactly the marker it writes, and refusing a file, a non-empty directory without it, or one whose marker is a link, a directory or other content, before a runtime reads it); exposes only a data directory, a GitHub token, a log level, a logger, a runtime source and an installed CLI path, leaving every client-level channel of injected capability unset; reports the installed CLI it runs and the version and protocol the started runtime reports; refuses a started runtime that is not signed in, naming every way to sign it in; explains a runtime that cannot start for what was launched - a missing direct SDK reference for the bundled runtime, the CLI and its version for an installed CLI, a CLI too old or too new for a protocol mismatch - and the sources it authenticates from; passes cancellation through unchanged; lets the host delete a conversation the runtime kept, by session identifier, without exposing its client; releases the client when a start fails or is canceled; and disposes it once, after which it cannot be used. Agents, provider-session factories and summarizers are built on it and never dispose it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntime.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntimeOptions.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntimeSource.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotRuntimeTests.cs", "test/DemaConsulting.AgentKit.Agents.Copilot.Tests/FakeCopilotCliMachine.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-runtime.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-runtime.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-runtime.yaml");
        }
    }

    // Resolving an installed CLI - discovery, the version probe and the minimum version - and the
    // guidance a refusal carries belong to the installed-CLI unit; the runtime decides which
    // executable to launch and launches it.
    dependency CopilotRuntime to CopilotCli;
}
