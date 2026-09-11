// Views rendered into docs/design/generated/ for the Design document.
// Reopens the AgentKitCore package so each 'expose' statement can reference
// its target part def directly. Per SysML2Tools syntax, 'expose' is only valid
// inside a named 'view' usage (not a 'view def' definition), and it - not
// 'render' - is what scopes the rendered diagram content.
package AgentKitCore {
    view SoftwareStructureView {
        expose AgentKitCore;
        expose AgentKitTools;
        expose AgentKitAgentsChatClient;
        expose AgentKitAgentsCopilot;
    }

    view AgentKitCoreView {
        expose AgentKitCoreSystem;
    }
}

package AgentKitTools {
    view AgentKitToolsView {
        expose AgentKitToolsSystem;
    }

    view TextFileView {
        expose TextFile;
    }

    view ImageView {
        expose Image;
    }
}

package AgentKitAgentsChatClient {
    view AgentKitAgentsChatClientView {
        expose AgentKitAgentsChatClientSystem;
    }
}

package AgentKitAgentsCopilot {
    view AgentKitAgentsCopilotView {
        expose AgentKitAgentsCopilotSystem;
    }
}
