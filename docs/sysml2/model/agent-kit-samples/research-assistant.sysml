package AgentKitSamples {
    part def ResearchAssistant {
        doc /* Console sample demonstrating the agent-infrastructure path: how an agent plans, remembers, and delegates safely across turns using the todo, memory, and agent families over a read-only corpus and a separate writable notes location, and how an application supplies the offline embedding backend the memory family requires. */

        comment sourceRef /* Source: samples/research-assistant/ (a folder of several source files, not a single file: a sample is a complete runnable application read as one worked example) */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests/ (the sample's test project folder) */
        comment designRef /* Design: docs/design/agent-kit-samples/research-assistant.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-samples/research-assistant.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-samples/research-assistant.yaml */
    }
}
