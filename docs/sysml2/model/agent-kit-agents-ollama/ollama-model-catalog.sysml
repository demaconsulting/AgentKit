package AgentKitAgentsOllama_OllamaModelCatalog {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsOllama_OllamaContextWindow::*;

    part def OllamaModelCatalog {
        doc /* Describes the models an Ollama server holds as a provider-neutral model catalog: what each can do as the server reports it (inferred from projector or vision metadata for a server too old to say), whether an agent can run on it, the reasoning efforts a thinking model honors, and the length it was trained for, which is reported only as the ceiling a stated window is refused above and never as a window to account against. Models are listed in the order the server lists them, a model the server will not describe is listed as unusable rather than failing the listing, and a bare name finds its latest tag. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Ollama/OllamaModelCatalog.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Ollama.Tests/OllamaModelCatalogTests.cs");
            designFiles = ("docs/design/agent-kit-agents-ollama/ollama-model-catalog.md");
            verificationFiles = ("docs/verification/agent-kit-agents-ollama/ollama-model-catalog.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-ollama/ollama-model-catalog.yaml");
        }
    }

    // The catalog is a Core caching catalog producing Core model descriptions, and resolves a bare
    // model name exactly as the window reading does.
    dependency OllamaModelCatalog to AgentKitCore_ModelCatalog::ModelCatalog;
    dependency OllamaModelCatalog to AgentKitCore_ModelInfo::ModelInfo;
    dependency OllamaModelCatalog to OllamaContextWindow;
}