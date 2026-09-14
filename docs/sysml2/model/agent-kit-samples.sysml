package AgentKitSamples {
    doc
    /*
     * AgentKitSamples is the repository's set of runnable demonstration applications. Each sample is
     * a self-contained console application that exercises AgentKit end to end against a live model,
     * and each answers a different question a reader brings to the library: how to consume it
     * (DocumentAssistant), how an agent works across turns (ResearchAssistant), and how to extend it
     * (CustomTools). The samples are delivered as source rather than as a published package, so
     * their requirements are demonstration requirements. The system is deliberately flat: the three
     * samples share no code, so a subsystem layer would add artifacts without carrying architectural
     * information.
     */

    part def AgentKitSamplesSystem {
        doc /* The AgentKit demonstration samples as a whole. Each sample is one unit. */

        // No testRef: the samples share no code and have no system-level integration test project;
        // each sample is verified through its own unit test project (see the unit part defs).
        comment designRef /* Design: docs/design/agent-kit-samples.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-samples.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-samples.yaml */

        part customTools : CustomTools;
        part documentAssistant : DocumentAssistant;
        part researchAssistant : ResearchAssistant;
    }
}
