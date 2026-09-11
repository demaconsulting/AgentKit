package AgentKitCore {
    part def RealPathResolver {
        doc /* Resolves a path to its real file system location, following symbolic links and directory junctions at every path component so that access is judged on the location actually reached. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/RealPathResolver.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/RealPathResolverTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/real-path-resolver.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/real-path-resolver.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/real-path-resolver.yaml */
    }
}
