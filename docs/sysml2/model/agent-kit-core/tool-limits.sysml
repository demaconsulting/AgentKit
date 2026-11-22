package AgentKitCore_ToolLimits {
    private import SoftwareItemMetadata::*;

    part def ToolLimits {
        doc /* Carries the ceilings a tool observes when reading, returning, decoding and attaching content, and the bound on how many entries one recursive removal may take, reasoning from the model's context budget, from the host resource a decode spends, and from the damage a mistaken removal would do rather than byte convenience, and travelling with the access policy so every tool observes the same budget. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolLimits.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolLimitsTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-limits.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-limits.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-limits.yaml");
        }
    }
}
