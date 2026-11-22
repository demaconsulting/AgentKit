package AgentKitCore_AgentSession {
    private import SoftwareItemMetadata::*;

    part def AgentSession {
        doc /* The session contract an application programs against - send a message, read what the window looks like, dispose - together with what one turn reports back about the answer, the resulting usage, whether the session rotated, the compaction level it is at and whether the turn had to drop history outright. Deliberately small and provider-silent, so an application wanting a long-running agent need not know how compaction is achieved. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/AgentSession.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/AgentSessionTests.cs");
            designFiles = ("docs/design/agent-kit-core/agent-session.md");
            verificationFiles = ("docs/verification/agent-kit-core/agent-session.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/agent-session.yaml");
        }
    }
}