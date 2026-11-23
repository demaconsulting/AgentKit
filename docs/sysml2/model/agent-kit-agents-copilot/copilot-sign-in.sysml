package AgentKitAgentsCopilot_CopilotSignIn {
    private import SoftwareItemMetadata::*;

    part def CopilotSignIn {
        doc /* How the Copilot runtime AgentKit starts signs in to GitHub: uses the first source that applies - a token or token provider the host supplies, a token in COPILOT_GITHUB_TOKEN, GH_TOKEN or GITHUB_TOKEN, the user's Copilot CLI sign-in unless the host turned it off, then the runtime's own fallbacks (a sign-in stored in the data directory and the GitHub CLI) - each with the GitHub host that belongs to it, skipping a Copilot CLI account on another host than the one requested; reads only the signed-in account's host and login from the Copilot CLI's configuration and obtains its token through a short-lived helper runtime of the same executable, in the only mode that reads the system credential store, on a new temporary home holding only that account entry, with every token and host variable withheld and the GitHub CLI hidden, opening no session, used only when it reports exactly that account signed in through its stored sign-in, and stopped with its home deleted whatever happens; hands the confined runtime only the token and the host; records a step that cannot be used and lets the next apply, propagating only cancellation; asks the host's token provider at start and gives each session a provider that asks it; and explains a runtime no source signed in by listing what each source found, with no token in any message, log entry or string form; refuses a COPILOT_GH_HOST that is not a valid GitHub host unless the host chose one, and hands the runtime a valid one as the normalized bare host name; records a COPILOT_HOME that is not a valid path as a finding; redacts every token it knows, and any text shaped like a GitHub token, from runtime and SDK text other units quote, and makes the redacted copy - type name, message and stack trace, with each chained exception copied the same way and nothing else - that is kept as an inner exception in place of a runtime or SDK failure; and relays a runtime or SDK failure to a host as a new exception of the same kind - an I/O failure, a timeout, an invalid operation, a cancellation with its token or a disposal, and any other type as an invalid operation - with a redacted message and that copy as its cause, passing unchanged only a cancellation or disposal carrying nothing to redact or a failure already built around a redacted copy. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSignIn.cs", "src/DemaConsulting.AgentKit.Agents.Copilot/CopilotSignInSource.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotSignInTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-sign-in.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-sign-in.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-sign-in.yaml");
        }
    }

    // The options the host gave, the launch the helper runs the same executable as, and the confined
    // runtime's environment the helper's is built from belong to the runtime unit. Qualified rather
    // than imported, because the runtime unit imports this package.
    dependency CopilotSignIn to AgentKitAgentsCopilot_CopilotRuntime::CopilotRuntime;

    // Finding the GitHub CLI and node on PATH follows the installed-CLI unit's safe search rules.
    dependency CopilotSignIn to AgentKitAgentsCopilot_CopilotCli::CopilotCli;
}
