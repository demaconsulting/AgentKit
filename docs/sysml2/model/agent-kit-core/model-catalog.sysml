package AgentKitCore_ModelCatalog {
    private import SoftwareItemMetadata::*;
    private import AgentKitCore_ModelInfo::*;
    private import AgentKitCore_ModelSettings::*;

    part def ModelCatalog {
        doc /* Defines the provider-neutral model catalog: a listing filtered by the capabilities an application requires, a lookup by identifier, and a refresh; a caching base the provider catalogs share, which reads lazily, keeps a listing for five minutes, shares one read among concurrent callers, and caches no failure; and the session-start gate that looks persisted settings up and validates them against the model as the provider now describes it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ModelCatalog.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ModelCatalogTests.cs");
            designFiles = ("docs/design/agent-kit-core/model-catalog.md");
            verificationFiles = ("docs/verification/agent-kit-core/model-catalog.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/model-catalog.yaml");
        }
    }

    // A catalog lists and finds model descriptions, and its session-start gate binds settings into
    // a validated selection.
    dependency ModelCatalog to ModelInfo;
    dependency ModelCatalog to ModelSettings;
}