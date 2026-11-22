package AgentKitCore_ContextLayout {
    private import SoftwareItemMetadata::*;

    part def ContextLayout {
        doc /* The whole of a session context as this library accounts for it as a round-robin structure - a fixed number of tiers each holding a fixed number of consolidated slots, and the verbatim recent turns - together with the most-stable-first seed a fresh provider session is created from. It holds the shape and publishes the counts; the rules that keep a tier at its complement, including the ring at the coarsest tier, belong to the rotation engine. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ContextLayout.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ContextLayoutTests.cs");
            designFiles = ("docs/design/agent-kit-core/context-layout.md");
            verificationFiles = ("docs/verification/agent-kit-core/context-layout.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/context-layout.yaml");
        }
    }
}