package AgentKitAgentsCopilot_CopilotCli {
    private import SoftwareItemMetadata::*;

    part def CopilotCli {
        doc /* The installed GitHub Copilot CLI the runtime runs by default: finds it on the search path and then in the platform's well-known installation locations (WinGet and npm on Windows; Homebrew, the install script and npm on macOS and Linux), choosing the first candidate that exists and can be started without a shell, never searching a search-path entry resolved against the current directory (a relative entry or, on Windows, a drive-relative one) and never consulting COPILOT_CLI_PATH; passes over and reports an editor's Copilot launcher; reports every location searched; reads the version the CLI runs when started without automatic updates, without a shell, under the runtime's withheld environment and data directory, within a bounded time, stopping what a timed-out or canceled probe started, and through Node.js for an npm installation; holds the minimum version, the first that speaks the SDK protocol the library supports; and composes the guidance a refusal carries - where it looked, the version found and required, how to check the version the library runs, how to install or upgrade per installation method, and the alternatives of naming a CLI or using the bundled runtime; redacts every refusal of the version probe, and the error output it quotes before its end is cut, of the probe environment's secret values and any text shaped like a GitHub token, keeping only a redacted copy of a failure to start as the cause. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotCli.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotCliSearchResult.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotCliProcessTree.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/FakeCopilotCliMachine.cs", "test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotCliTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-cli.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-cli.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-cli.yaml");
        }
    }

    // The probe's refusals are redacted, and a start failure kept only as a redacted copy, with the
    // sign-in unit's secrets and redaction. Qualified rather than imported, because the sign-in unit
    // depends on this one.
    dependency CopilotCli to AgentKitAgentsCopilot_CopilotSignIn::CopilotSignIn;
}
