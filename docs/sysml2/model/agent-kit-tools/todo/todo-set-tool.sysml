package AgentKitTools {
    part def TodoSetTool {
        doc /* Publishes the todo_set tool, adding a step or replacing the step already carrying that id in place, defaulting the status to pending, accepting exactly the four published statuses, returning a plain statement of the id, the recorded status and the size of the list, and refusing every malformed request as a returned denial rather than throwing. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Todo/TodoSetTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoSetToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo/todo-set-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo/todo-set-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo/todo-set-tool.yaml */
    }
}
