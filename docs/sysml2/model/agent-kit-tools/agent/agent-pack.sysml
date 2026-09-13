package AgentKitTools {
    part def AgentPack {
        doc /* Publishes the agent family as one pack under the agent family prefix, requires the host's delegation capability, holds the profiles and the runner the application registered, validates that no profile widens the parent's policy, and composes each child from a fresh creation of the registered child packs against the child's own policy. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Agent/AgentPack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Agent/AgentPackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/agent/agent-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/agent/agent-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/agent/agent-pack.yaml */
    }
}
