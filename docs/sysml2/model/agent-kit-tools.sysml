package AgentKitTools {
    private import SoftwareItemMetadata::*;
    public import AgentKitTools_TextFileSubsystem::*;
    public import AgentKitTools_FileSubsystem::*;
    public import AgentKitTools_MarkdownSubsystem::*;
    public import AgentKitTools_TodoSubsystem::*;
    public import AgentKitTools_MemorySubsystem::*;
    public import AgentKitTools_AgentSubsystem::*;

    doc
    /*
     * AgentKitTools is a general-purpose capability package of guarded tool families,
     * composed through the AgentKitCore contract. It is a peer of the other capability
     * packages an application may attach, depending on AgentKitCore but never depended
     * upon by another capability package. It declares the six tool families the package
     * ships and references no imaging library; the image family ships in AgentKitToolsVisual.
     */

    part def AgentKitToolsSystem {
        doc /* The AgentKit Tools system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/AgentKitToolsTests.cs");
            designFiles = ("docs/design/agent-kit-tools.md");
            verificationFiles = ("docs/verification/agent-kit-tools.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools.yaml");
        }

        part textFile : TextFile;
        part file : File;
        part markdown : Markdown;
        part todo : Todo;
        part memory : Memory;
        part agent : Agent;
    }
}
