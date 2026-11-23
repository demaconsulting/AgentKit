package AgentKitAgentsCopilot_CopilotRuntime {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotCli::*;
    private import AgentKitAgentsCopilot_CopilotSignIn::*;

    part def CopilotRuntime {
        doc /* The Copilot runtime AgentKit starts, confines and owns, with the options a host may give it: builds the only Copilot client the package runs on, in Empty mode, spawning as a child process over standard input and output either the installed Copilot CLI (the default: the CLI the host names or the one discovery finds, refused before any client exists when it is missing or older than the supported minimum) or, only when the host chooses it, the runtime Microsoft.Copilot.SDK provisions into the application's output, never an executable the host's environment names, with the same mode, environment and data directory for either; the environment is the host's minus every COPILOT_ and GITHUB_COPILOT_ variable apart from a documented credential, network and telemetry-off pass-through set, on a data directory it creates and marks (reusing a directory carrying exactly the marker it writes, and refusing a file, a non-empty directory without it, or one whose marker is a link, a directory or other content, before a runtime reads it); exposes only a data directory, a GitHub token or token provider, a GitHub host, whether the user's Copilot CLI sign-in may be used, a log level, a logger, a runtime source and an installed CLI path, leaving every client-level channel of injected capability unset, and refuses a token given with a token provider or a host that is not an https host; starts the runtime with the token and GitHub host the sign-in resolution chose; reports the installed CLI it runs, the version and protocol the started runtime reports, and the sign-in source, host and login it signed in with; gives each session it opens a token provider that asks the host's when it was started with one; refuses a started runtime that is not signed in, listing what each sign-in source found, and relays a failed read of its status redacted; gives the SDK the host's logger only behind the sign-in's redacting logger, so everything the SDK logs reaches the host redacted; redacts its launch refusals with the host environment's secrets and the host's token; explains, redacted as a whole, a runtime that cannot start for what was launched - a missing direct SDK reference for the bundled runtime, the CLI and its version for an installed CLI, a CLI too old or too new for a protocol mismatch - and the sources it authenticates from; passes cancellation through unchanged; lets the host delete a conversation the runtime kept, by session identifier, without exposing its client; releases the client when a start fails or is canceled; and disposes it once, after which it cannot be used. Agents, provider-session factories and summarizers are built on it and never dispose it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntime.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntimeOptions.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotRuntimeSource.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotGitHubToken.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotGitHubTokenRequest.cs");
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

    // How the runtime signs in - the source order, the host that goes with each source, the Copilot
    // CLI token helper, the refusal's text and the session token provider - belongs to the sign-in
    // unit; the runtime starts with what it resolved.
    dependency CopilotRuntime to CopilotSignIn;
}
