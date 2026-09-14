package AgentKitSamples {
    part def DocumentAssistant {
        doc /* Console chat sample demonstrating the consumption path: how an application attaches AgentKit's shipped tool packs to an agent under a policy that grants a workspace to read and a separate session location to write, making containment, asymmetric grants, capability gating, and the path dialect observable. */

        comment sourceRef /* Source: samples/document-assistant/ (a folder of several source files, not a single file: a sample is a complete runnable application read as one worked example) */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests/ (the sample's test project folder) */
        comment designRef /* Design: docs/design/agent-kit-samples/document-assistant.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-samples/document-assistant.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-samples/document-assistant.yaml */
    }
}
