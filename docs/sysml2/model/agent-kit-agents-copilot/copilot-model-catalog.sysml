package AgentKitAgentsCopilot_CopilotModelCatalog {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsCopilot_CopilotRuntime::*;

    part def CopilotModelCatalog {
        doc /* Describes the models the GitHub Copilot service offers the signed-in user as a provider-neutral model catalog, read uncached through the runtime AgentKit started and refusing a disposed runtime: vision only when the service reports it, tool calling unless the service denies it (a documented provider-specific rule), reasoning efforts by the service's own identifiers with no invented default, context tiers derived from billing with AgentKit's labels and windows capped at the model's largest, usability from the account's policy, and the service's notices. The automatic model choice is not listed. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Copilot/CopilotModelCatalog.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Copilot.Tests/CopilotModelCatalogTests.cs");
            designFiles = ("docs/design/agent-kit-agents-copilot/copilot-model-catalog.md");
            verificationFiles = ("docs/verification/agent-kit-agents-copilot/copilot-model-catalog.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-copilot/copilot-model-catalog.yaml");
        }
    }

    // The catalog is a Core caching catalog producing Core model descriptions, and reads the service
    // through the runtime AgentKit owns, taking its client at every read and never disposing it.
    dependency CopilotModelCatalog to AgentKitCore_ModelCatalog::ModelCatalog;
    dependency CopilotModelCatalog to AgentKitCore_ModelInfo::ModelInfo;
    dependency CopilotModelCatalog to CopilotRuntime;

    // A listing failure keeps only a redacted copy of the client's failure as its cause, made with
    // the runtime's sign-in, and any other read failure is relayed as a redacted copy.
    dependency CopilotModelCatalog to AgentKitAgentsCopilot_CopilotSignIn::CopilotSignIn;
}