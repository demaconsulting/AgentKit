package AgentKitAgentsOllama {
    part def OllamaContextWindow {
        doc /* Reports the context window an Ollama server will enforce for a model, preferring a window the application stated, then the length the server loaded that model with, then the model's published maximum, and naming a conservative default as assumed when none of those is reported. Reading the server and choosing among what it reported are separate, so the precedence is exercisable without a server. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Agents.Ollama/OllamaContextWindow.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Ollama.Tests/OllamaContextWindowTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-ollama/ollama-context-window.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-ollama/ollama-context-window.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-ollama/ollama-context-window.yaml */
    }
}
