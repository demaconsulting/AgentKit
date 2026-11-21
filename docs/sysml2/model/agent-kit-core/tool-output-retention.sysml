package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ToolOutputRetention {
        doc /* One conversation's retention of large tool outputs as a handle an application owns: wraps tools added at any time into one store so an oversized output is retained and previewed, publishes the two reader tools once, and exports the retained outputs and reference counter as a JSON-serializable snapshot that restores under the current ceilings without ever re-issuing a reference; ToolOutputSpill composes through it. Its snapshot types are documented as supporting value types in its own unit doc. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputRetention.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputRetentionTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-retention.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-retention.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-retention.yaml");
        }
    }
}
