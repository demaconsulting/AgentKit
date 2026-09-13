package AgentKitTools {
    part def AgentRunTool {
        doc /* Publishes the agent_run tool, which starts one of the application's registered agents on a task and returns what it said; refuses an absent request, an unknown profile, and a call at the delegation depth ceiling; and composes each child's tools through an internal seam so that no expression at this call site can hand a parent's tool list down. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Agent/AgentRunTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentRunToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/agent/agent-run-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/agent/agent-run-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/agent/agent-run-tool.yaml */
    }
}
