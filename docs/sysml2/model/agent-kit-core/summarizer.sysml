package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def Summarizer {
        doc /* The injected out-of-session consolidation contract, the request that carries the material as a body of peers together with an aggressiveness clause and the target tier, and the consolidation prompt that licenses collapsing repetition, names the specific content that must survive, and asks for terseness by instruction rather than a target size. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/Summarizer.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/SummarizerTests.cs");
            designFiles = ("docs/design/agent-kit-core/summarizer.md");
            verificationFiles = ("docs/verification/agent-kit-core/summarizer.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/summarizer.yaml");
        }
    }
}