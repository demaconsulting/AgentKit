package AgentKitTools {
    part def Agent {
        doc /* The agent tool family: application-authored delegation from one agent to another, published as one pack under the agent family prefix, gated on the host's delegation capability, and bounded by the agent depth ceiling so that a child's tools are always composed from the child's own state rather than filtered from the parent's. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/agent.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/agent.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/agent.yaml */

        part agentProfile : AgentProfile;
        part agentRunTool : AgentRunTool;
        part agentPack : AgentPack;
    }
}
