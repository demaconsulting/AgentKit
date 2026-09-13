package AgentKitTools {
    part def Todo {
        doc /* The todo tool family: an in-memory flat task list one agent can write down, advance and close out, published as one pack under the todo family prefix, requiring no host capability and consulting no path policy. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo.yaml */

        part todoStore : TodoStore;
        part todoListTool : TodoListTool;
        part todoSetTool : TodoSetTool;
        part todoRemoveTool : TodoRemoveTool;
        part todoPack : TodoPack;
    }
}
