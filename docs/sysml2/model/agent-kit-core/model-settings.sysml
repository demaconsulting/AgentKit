package AgentKitCore {
    private import SoftwareItemMetadata::*;
    private import ScalarValues::*;

    part def ModelSettings {
        doc /* Holds the choices a user makes about the model an agent runs on - the model, and optionally a reasoning effort, a context tier, a context window and an output limit - as plain data an application persists; validates them against a model, reporting every problem in words a user reads; and binds them into a validated selection, the only form a provider factory accepts, together with the exception that lists every reason settings cannot be applied. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ModelSettings.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ModelSettingsTests.cs");
            designFiles = ("docs/design/agent-kit-core/model-settings.md");
            verificationFiles = ("docs/verification/agent-kit-core/model-settings.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/model-settings.yaml");
        }

        attribute modelId : String;
        attribute reasoningEffort : String [0..1];
        attribute contextTier : String [0..1];
        attribute contextWindowTokens : Integer [0..1];
        attribute maxOutputTokens : Integer [0..1];
    }

    // Settings are validated against a model description and the capabilities it reports.
    dependency ModelSettings to ModelInfo;
}