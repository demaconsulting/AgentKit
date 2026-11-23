// Views rendered into docs/design/generated/ for the Design document.
// One repository-level views package imports each system and subsystem package so every
// 'expose' statement can reference its target part def directly. Per SysML2Tools syntax,
// 'expose' is only valid inside a named 'view' usage (not a 'view def' definition), and it -
// not 'render' - is what scopes the rendered diagram content. SoftwareStructureView is an
// InterconnectionView overview: each system renders as a frame with its direct members.
// The per-system and per-subsystem views expose the container and its direct children and
// render as interconnection diagrams so nested composition renders correctly.
package AgentKitDesignViews {
    private import StandardViewDefinitions::*;
    private import Views::*;
    private import AgentKitCore::*;
    private import AgentKitTools::*;
    private import AgentKitToolsVisual::*;
    private import AgentKitAgentsChatClient::*;
    private import AgentKitAgentsCopilot::*;
    private import AgentKitAgentsOllama::*;
    private import AgentKitTools_TextFileSubsystem::*;
    private import AgentKitTools_FileSubsystem::*;
    private import AgentKitTools_MarkdownSubsystem::*;
    private import AgentKitTools_TodoSubsystem::*;
    private import AgentKitTools_MemorySubsystem::*;
    private import AgentKitTools_AgentSubsystem::*;
    private import AgentKitToolsVisual_ImageSubsystem::*;
    private import AgentKitToolsVisual_DiagramSubsystem::*;
    private import AgentKitToolsVisual_RenderingSubsystem::*;

    view SoftwareStructureView : InterconnectionView {
        expose AgentKitCore::AgentKitCoreSystem;
        expose AgentKitTools::AgentKitToolsSystem;
        expose AgentKitToolsVisual::AgentKitToolsVisualSystem;
        expose AgentKitAgentsChatClient::AgentKitAgentsChatClientSystem;
        expose AgentKitAgentsCopilot::AgentKitAgentsCopilotSystem;
        expose AgentKitAgentsOllama::AgentKitAgentsOllamaSystem;
        filter not (@SysML::Documentation);
    }

    view AgentKitCoreView {
        expose AgentKitCoreSystem;
        expose AgentKitCoreSystem::*;
        render asInterconnectionDiagram;
    }

    view AgentKitToolsView {
        expose AgentKitToolsSystem;
        expose AgentKitToolsSystem::*;
        render asInterconnectionDiagram;
    }

    view AgentKitToolsVisualView {
        expose AgentKitToolsVisualSystem;
        expose AgentKitToolsVisualSystem::*;
        render asInterconnectionDiagram;
    }

    view AgentKitAgentsChatClientView {
        expose AgentKitAgentsChatClientSystem;
        expose AgentKitAgentsChatClientSystem::*;
        render asInterconnectionDiagram;
    }

    view AgentKitAgentsCopilotView {
        expose AgentKitAgentsCopilotSystem;
        expose AgentKitAgentsCopilotSystem::*;
        render asInterconnectionDiagram;
    }

    view AgentKitAgentsOllamaView {
        expose AgentKitAgentsOllamaSystem;
        expose AgentKitAgentsOllamaSystem::*;
        render asInterconnectionDiagram;
    }

    view TextFileView {
        expose TextFile;
        expose TextFile::*;
        render asInterconnectionDiagram;
    }

    view FileView {
        expose File;
        expose File::*;
        render asInterconnectionDiagram;
    }

    view MarkdownView {
        expose Markdown;
        expose Markdown::*;
        render asInterconnectionDiagram;
    }

    view ImageView {
        expose Image;
        expose Image::*;
        render asInterconnectionDiagram;
    }

    view DiagramView {
        expose Diagram;
        expose Diagram::*;
        render asInterconnectionDiagram;
    }

    view RenderingView {
        // Qualified: the standard Views library also declares a 'Rendering'.
        expose AgentKitToolsVisual_RenderingSubsystem::Rendering;
        expose AgentKitToolsVisual_RenderingSubsystem::Rendering::*;
        render asInterconnectionDiagram;
    }

    view TodoView {
        expose Todo;
        expose Todo::*;
        render asInterconnectionDiagram;
    }

    view MemoryView {
        expose Memory;
        expose Memory::*;
        render asInterconnectionDiagram;
    }

    view AgentView {
        expose Agent;
        expose Agent::*;
        render asInterconnectionDiagram;
    }
}
