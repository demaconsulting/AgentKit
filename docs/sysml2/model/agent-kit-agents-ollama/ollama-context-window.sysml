package AgentKitAgentsOllama_OllamaContextWindow {
    private import SoftwareItemMetadata::*;

    part def OllamaContextWindow {
        doc /* Reports the context window an Ollama server will enforce for a model, preferring a window the application stated, then the length the running instance reports, and naming a conservative default as assumed when neither is available. What a model file publishes as its maximum is deliberately never consulted, because it describes the file rather than the instance. Reading the server and choosing among what it reported are separate, so the precedence is exercisable without a server. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Ollama/OllamaContextWindow.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Ollama.Tests/OllamaContextWindowTests.cs");
            designFiles = ("docs/design/agent-kit-agents-ollama/ollama-context-window.md");
            verificationFiles = ("docs/verification/agent-kit-agents-ollama/ollama-context-window.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-ollama/ollama-context-window.yaml");
        }
    }

    // The window reading takes the model and its stated window from a validated model selection.
    dependency OllamaContextWindow to AgentKitCore::ModelSettings;
}
