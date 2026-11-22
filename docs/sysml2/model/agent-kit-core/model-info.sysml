package AgentKitCore_ModelInfo {
    private import SoftwareItemMetadata::*;
    private import AgentKitCore_ToolPack::*;
    private import ScalarValues::*;

    enum def ModelCapabilities {
        doc /* What a model can do, as its provider reports it; combinable flags, each set only on a positive signal unless a provider documents otherwise. */

        enum None;
        enum ToolCalls;
        enum Vision;
        enum Reasoning;
    }

    enum def ModelAdjustments {
        doc /* Which model settings a provider can honor for a particular model; combinable flags. */

        enum None;
        enum ReasoningEffort;
        enum ContextTier;
        enum ContextWindow;
        enum MaxOutputTokens;
    }

    enum def ModelNoticeKind {
        doc /* How prominently an application shows a notice about a model. */

        enum Info;
        enum Warning;
        enum Required;
    }

    part def ModelInfo {
        doc /* Describes one model a provider offers in provider-neutral terms an application can filter on, show in a picker, and validate settings against: its identity, capabilities, the settings it accepts, the reasoning efforts, context tiers, window and output limits it offers, whether AgentKit can run an agent on it and why not, its billing and grouping, and the notices to show. Also derives the vision host capability from a model that accepts images. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ModelInfo.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ModelInfoTests.cs");
            designFiles = ("docs/design/agent-kit-core/model-info.md");
            verificationFiles = ("docs/verification/agent-kit-core/model-info.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/model-info.yaml");
        }

        attribute provider : String;
        attribute id : String;
        attribute displayName : String;
        attribute vendor : String [0..1];
        attribute capabilities : ModelCapabilities;
        attribute adjustments : ModelAdjustments;
        attribute isUsable : Boolean;
        attribute unusableReason : String [0..1];
        attribute contextWindowTokens : Integer [0..1];
        attribute maxOutputTokens : Integer [0..1];
        attribute reasoningEffortIds : String [0..*];
        attribute defaultReasoningEffort : String [0..1];
        attribute contextTierIds : String [0..*];
        attribute defaultContextTier : String [0..1];
        attribute billingMultiplier : Real [0..1];
        attribute category : String [0..1];
        attribute priceCategory : String [0..1];
        attribute noticeKinds : ModelNoticeKind [0..*];
        // Provider-specific extras, keyed by a name each catalog documents.
        attribute additionalProperties_key : String [0..*];
        attribute additionalProperties_value : String [0..*];
    }

    // The derived host capability is the vision member of the host-capability vocabulary tool packs
    // are gated on.
    dependency ModelInfo to ToolPack;
}