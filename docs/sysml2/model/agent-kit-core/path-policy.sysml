package AgentKitCore_PathPolicy {
    private import SoftwareItemMetadata::*;

    part def PathPolicy {
        doc /* Holds the one working directory relative paths are anchored to and the zero-or-more access grants that permit locations, keeping addressing and permission orthogonal, and provides the single containment decision used by both direct path access and directory enumeration, reporting a refusal as a returned denial that echoes the request, states any interpretation, and enumerates the permitted locations, both relative to the working directory so no host absolute path is disclosed, rather than as an exception. Alongside the lexical read and write decisions it offers link-following ones that also require a grant of the required access to permit the location a path reaches through its links, as a pre-flight check. It also answers two questions about the policy as a whole rather than about any path: whether the working directory is itself granted, and whether any grant permits writing anywhere at all, so a tool pack can decide up front which of its tools the policy leaves able to write. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/PathPolicy.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/PathPolicyTests.cs");
            designFiles = ("docs/design/agent-kit-core/path-policy.md");
            verificationFiles = ("docs/verification/agent-kit-core/path-policy.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/path-policy.yaml");
        }
    }
}
