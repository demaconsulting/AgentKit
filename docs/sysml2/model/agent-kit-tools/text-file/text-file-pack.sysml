package AgentKitTools {
    part def TextFilePack {
        doc /* Publishes the text_file tool family as one pack: the search, read, create, replace, cut-lines and paste-lines tools in a fixed order, each governed by the supplied policy, with one cut/paste buffer allocated per composition and shared by the cut and paste tools. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFilePack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFilePackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-pack.yaml */
    }
}
