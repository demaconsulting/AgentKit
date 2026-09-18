package AgentKitAgentsOllama {
    doc
    /*
     * AgentKitAgentsOllama reports the context window a conversation on an Ollama model should
     * be accounted against, and which figure that came from. An IChatClient publishes no
     * context window, so a compacting session has to be told one; Ollama will say, but only
     * through its own APIs. This system exists so that capability can be depended upon by an
     * application rather than copied out of a sample, and so the OllamaSharp dependency it
     * carries stays out of the IChatClient adapter that serves every provider in that family
     * alike.
     */

    part def AgentKitAgentsOllamaSystem {
        doc /* The AgentKit Ollama Agents system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Agents.Ollama.Tests/AgentKitAgentsOllamaTests.cs */
        comment designRef /* Design: docs/design/agent-kit-agents-ollama.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-agents-ollama.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-agents-ollama.yaml */

        part ollamaContextWindow : OllamaContextWindow;
    }
}
