package AgentKitTools {
    doc
    /*
     * AgentKitTools is a general-purpose capability package of guarded tool families,
     * composed through the AgentKitCore contract. It is a peer of the other capability
     * packages an application may attach, depending on AgentKitCore but never depended
     * upon by another capability package. It declares the tool families the package ships.
     */

    part def AgentKitToolsSystem {
        doc /* The AgentKit Tools system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/AgentKitToolsTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools.yaml */

        part textFile : TextFile;
        part file : File;
        part markdown : Markdown;
        part image : Image;
        part todo : Todo;
        part memory : Memory;
        part agent : Agent;
    }
}
