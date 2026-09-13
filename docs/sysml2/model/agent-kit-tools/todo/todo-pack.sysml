package AgentKitTools {
    part def TodoPack {
        doc /* Publishes the todo tool family as one pack: list, set and remove in that order, allocating a fresh in-memory task list on every CreateTools call so a delegated agent keeps its own, requiring no host capability, accepting a policy it never consults, and publishing the suggested instruction an application should give an agent that carries the family. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Todo/TodoPack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoPackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo/todo-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo/todo-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo/todo-pack.yaml */
    }
}
