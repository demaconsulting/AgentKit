package AgentKitCore {
    part def ToolLimits {
        doc /* Carries the ceilings a tool observes when reading, returning and attaching content, reasoning from the model's context budget rather than byte convenience, and travelling with the access policy so every tool observes the same budget. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/ToolLimits.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/ToolLimitsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/tool-limits.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/tool-limits.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/tool-limits.yaml */
    }
}
