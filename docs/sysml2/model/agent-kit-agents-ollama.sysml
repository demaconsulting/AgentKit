package AgentKitAgentsOllama {
    private import SoftwareItemMetadata::*;
    private import AgentKitAgentsOllama_OllamaContextWindow::*;
    private import AgentKitAgentsOllama_OllamaContextSizingChatClient::*;

    doc
    /*
     * AgentKitAgentsOllama makes the context window a compacting session accounts against the
     * window the Ollama instance is actually using. An IChatClient publishes no context window,
     * so a session has to be told one; Ollama decides what an instance runs at when it loads the
     * model, and will report it, but only through its own APIs. This system asks the server to
     * run at a size the application chose, and reports the size a running instance is using where
     * none was chosen. It exists so that capability can be depended upon by an application rather
     * than copied out of a sample, and so the OllamaSharp dependency it carries stays out of the
     * IChatClient adapter that serves every provider in that family alike.
     */

    part def AgentKitAgentsOllamaSystem {
        doc /* The AgentKit Ollama Agents system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Agents.Ollama.Tests/AgentKitAgentsOllamaTests.cs");
            designFiles = ("docs/design/agent-kit-agents-ollama.md");
            verificationFiles = ("docs/verification/agent-kit-agents-ollama.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-ollama.yaml");
        }

        part ollamaContextWindow : OllamaContextWindow;
        part ollamaContextSizingChatClient : OllamaContextSizingChatClient;
    }
}
