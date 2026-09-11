package AgentKitCore {
    part def PathPolicy {
        doc /* Pairs an independent read rule and write rule and provides the single containment decision used by both direct path access and directory enumeration, reporting a refusal as a returned denial rather than an exception. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/PathPolicy.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/PathPolicyTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/path-policy.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/path-policy.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/path-policy.yaml */
    }
}
