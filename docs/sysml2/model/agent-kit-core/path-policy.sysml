package AgentKitCore {
    part def PathPolicy {
        doc /* Holds the one working directory relative paths are anchored to and the zero-or-more access grants that permit locations, keeping addressing and permission orthogonal, and provides the single containment decision used by both direct path access and directory enumeration, reporting a refusal as a returned denial that echoes the request, states any interpretation, and enumerates the permitted locations rather than as an exception. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/PathPolicy.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/PathPolicyTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/path-policy.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/path-policy.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/path-policy.yaml */
    }
}
