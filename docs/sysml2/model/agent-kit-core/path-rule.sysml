package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def PathRule {
        doc /* One access grant, either unrestricted or confined to a single location, carrying an access level (read-only or read-write) that is permission only and its own denied patterns, so that permitted locations and their access can be expressed independently of addressing. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/PathRule.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/PathRuleTests.cs");
            designFiles = ("docs/design/agent-kit-core/path-rule.md");
            verificationFiles = ("docs/verification/agent-kit-core/path-rule.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/path-rule.yaml");
        }
    }
}
