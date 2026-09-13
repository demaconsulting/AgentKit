package AgentKitTools {
    part def ChildAgentRequest {
        doc /* The bundle the library hands the host's runner for one delegated agent: the profile's name, the application-authored instructions, the tools the library composed against the child's own policy, the task the parent stated, and the child's delegation depth. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Agent/ChildAgentRequest.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Agent/ChildAgentRequestTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/agent/child-agent-request.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/agent/child-agent-request.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/agent/child-agent-request.yaml */
    }
}
