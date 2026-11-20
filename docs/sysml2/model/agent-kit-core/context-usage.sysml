package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ContextUsage {
        doc /* The one shape every provider session answers with - tokens occupied, window size, and the tokens the conversation occupies. Carrying the conversation is what keeps every rotation comparison within one currency, because the split is made by whoever produced the totals. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ContextUsage.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ContextUsageTests.cs");
            designFiles = ("docs/design/agent-kit-core/context-usage.md");
            verificationFiles = ("docs/verification/agent-kit-core/context-usage.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/context-usage.yaml");
        }
    }
}