package AgentKitAgentsOllama {
    part def OllamaContextWindow {
        doc /* Reports the context window an Ollama server will enforce for a model, preferring a window the application stated, then the length the running instance reports, and naming a conservative default as assumed when neither is available. What a model file publishes as its maximum is deliberately never consulted, because it describes the file rather than the instance. Reading the server and choosing among what it reported are separate, so the precedence is exercisable without a server. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Ollama/OllamaContextWindow.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Ollama.Tests/OllamaContextWindowTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-ollama/ollama-context-window.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-ollama/ollama-context-window.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-ollama/ollama-context-window.yaml */
    }
}
