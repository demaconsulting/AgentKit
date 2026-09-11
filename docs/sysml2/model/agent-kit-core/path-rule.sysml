package AgentKitCore {
    part def PathRule {
        doc /* One access grant, either unrestricted or confined to a single location, carrying an access level (read-only or read-write) that is permission only and its own denied patterns, so that permitted locations and their access can be expressed independently of addressing. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/PathRule.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/PathRuleTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/path-rule.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/path-rule.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/path-rule.yaml */
    }
}
