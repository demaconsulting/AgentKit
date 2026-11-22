package AgentKitAgentsOllama_OllamaContextSizingChatClient {
    private import SoftwareItemMetadata::*;

    part def OllamaContextSizingChatClient {
        doc /* Asks an Ollama server to run a model at the context length the application chose, by naming that length on every request forwarded to the server, so the window a compacting session accounts against is the window the running instance is actually using rather than one it was merely told. Every other setting the caller made reaches the server unchanged, and a length the caller named on a request is left as the caller named it. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.Ollama/OllamaContextSizingChatClient.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Ollama.Tests/OllamaContextSizingChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-agents-ollama/ollama-context-sizing-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-agents-ollama/ollama-context-sizing-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-ollama/ollama-context-sizing-chat-client.yaml");
        }
    }
}
