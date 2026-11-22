package AgentKitTools_TodoPack {
    private import SoftwareItemMetadata::*;

    part def TodoPack {
        doc /* Publishes the todo tool family as one pack: list, set and remove in that order, allocating a fresh in-memory task list on every CreateTools call so a delegated agent keeps its own, requiring no host capability, accepting a policy it never consults, and publishing the suggested instruction an application should give an agent that carries the family. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Todo/TodoPack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoPackTests.cs");
            designFiles = ("docs/design/agent-kit-tools/todo/todo-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools/todo/todo-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/todo/todo-pack.yaml");
        }
    }
}
