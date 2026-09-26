package AgentKitTools {
    part def TextFilePack {
        doc /* Publishes the text_file tool family as one pack: the search, read, create, write, replace, cut-lines, copy-lines and paste-lines tools in a fixed order, each governed by the supplied policy, with one line buffer allocated per composition and shared by the write, cut, copy and paste tools. The five tools that can act only by writing — create, write, replace, cut-lines and paste-lines — are published only when the policy permits writing in some location; search, read and copy-lines consult only the read decision and are published under every policy, keeping their relative order. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFilePack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFilePackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-pack.yaml */
    }
}
