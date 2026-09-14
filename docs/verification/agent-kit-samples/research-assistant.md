## ResearchAssistant Unit Verification Design

This document describes the unit-level verification strategy for the `research-assistant` sample.

### Verification Approach

The research-assistant sample is verified across four groups of scenario, none of which contacts a
live model. The first pins the instructions the sample builds, because they are not decoration: the
task-list family is used reliably only when told to imperatively, provenance survives a correction
only when the instructions demand the new document be cited, and recall noise is judged only when the
absence of a similarity floor is stated. The second pins the delegation boundary — the packs a child
is composed from, the profiles it may adopt, and the tool set of the final recall turn — because that
boundary is what keeps a child agent out of its parent's plan and record. The third pins the
command-line surface, which performs no I/O and so can be asserted whole. The fourth pins the offline
embedding backend and the descriptor-phrasing property the sample's headline demonstration rests on,
end to end through the sample's own composition rather than against bare cosine values.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, target `net10.0`
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no network access, credential, or live model is used. The embedding
  backend is the offline lexical generator and the memory store is in-process
- **File system**: None; the tests use fixture path strings unlike any real path
- **Isolation**: Each test constructs its own composition, generator, and store; no state is shared

### Acceptance Criteria

A unit test run passes when all scenarios below pass without error or exception beyond those
explicitly asserted (IEC 62304 §5.5.2). Any instruction that drops guidance the sample relies on, any
child composed from a state-carrying family, any recall turn that can read a document, any
command-line input accepted that should be refused or refused that should be accepted, any credential
printed or a blank credential treated as one, or any embedding property that fails to hold
constitutes a failure.

### Test Scenarios

#### AgentKitSamples-ResearchAssistant-InstructionsNameGrantedLocations: Both Locations Named With Access

**Test**: `AgentComposition_BuildInstructions_AnyRun_NamesBothLocationsWithAccess`

Asserts the corpus is named read-only, the notes location read-write, and the agent is told it cannot
edit the corpus.

#### AgentKitSamples-ResearchAssistant-InstructionsAppendPublishedGuidance: Published Guidance Appended Verbatim

**Test**: `AgentComposition_BuildInstructions_AnyRun_AppendsThePublishedSuggestedInstructions`

Asserts both families' published suggested-instruction constants appear verbatim.

#### AgentKitSamples-ResearchAssistant-InstructionsRequireProvenanceOnRevision: A Correction Cites the New Document

**Test**: `AgentComposition_BuildInstructions_AnyRun_RequiresARevisionToCiteTheNewDocument`

Asserts the revision tool is named for the changed-source case, the update tool is ruled out for it,
and the reason is stated.

#### AgentKitSamples-ResearchAssistant-InstructionsWarnRecallHasNoFloor: Recall Has No Similarity Floor

**Test**: `AgentComposition_BuildInstructions_AnyRun_WarnsThatRecallHasNoSimilarityFloor`

Asserts the absence of a floor is stated together with the instruction to say plainly when nothing is
recorded on a subject.

#### AgentKitSamples-ResearchAssistant-InstructionsRequireASubjectOnlyDescriptor: A Descriptor Names the Subject Only

**Test**: `AgentComposition_BuildInstructions_AnyRun_RequiresASubjectOnlyDescriptor`

Asserts the rule, the fields provenance belongs in instead, and the reason are all present.

#### AgentKitSamples-ResearchAssistant-InstructionsRequireConclusionsWrittenToNotes: Conclusions Written to Notes

**Test**: `AgentComposition_BuildInstructions_AnyRun_RequiresConclusionsToBeWrittenToNotes`

Asserts the writing tool, the notes location, and the moment to write are all named.

#### AgentKitSamples-ResearchAssistant-InstructionsRequirePlanBeforeWork: A Plan Precedes the Work

**Test**: `AgentComposition_BuildInstructions_AnyRun_ForbidsAPlanItemThatIsAlreadyDone`

Asserts both the prohibition on an already-finished plan item and the requirement to write the plan
before the work begins.

#### AgentKitSamples-ResearchAssistant-InstructionsDescribeDelegationTruthfully: Delegation Described Truthfully

**Test**: `AgentComposition_BuildInstructions_DelegationDisabled_DoesNotOfferASubAgent`

Asserts a delegating run names the delegation tool and states a child shares none of the parent's
state, while a solo run names no such tool at all.

#### AgentKitSamples-ResearchAssistant-ComposesTheThreeCrossTurnFamilies: Planning, Memory, and Delegation Compose

**Test**: `AgentComposition_BuildTools_DelegationEnabled_ComposesPlanningMemoryAndDelegation`

Asserts the todo, memory, delegation, and reading tools are all present in a delegating run.

#### AgentKitSamples-ResearchAssistant-GatesDelegationOnTheHostGrant: Delegation Is Capability-Gated

**Test**: `AgentComposition_BuildTools_DelegationDisabled_NeverCreatesTheSubAgentTool`

Asserts that with delegation withheld the planning and memory families remain while the delegation
tool is absent entirely.

#### AgentKitSamples-ResearchAssistant-ContainsDelegatedChild: A Delegated Child Is Contained

**Tests**: `AgentComposition_CreateChildPacks_AnyRun_WithholdsTheTodoMemoryAndAgentFamilies`,
`AgentComposition_CreateProfiles_Reader_NarrowsToTheCorpusAndReadsOnly`,
`AgentComposition_CreateProfiles_Summarizer_DeclaresNoTools`

Asserts the child packs exclude the todo, memory, and agent families; the reader profile is narrowed
to a single read-only corpus grant admitting only reading tools; and the summarizer profile declares
no tools.

#### AgentKitSamples-ResearchAssistant-RecallTurnCarriesMemoryOnly: The Recall Turn Carries Memory Only

**Tests**: `AgentComposition_BuildRecallTools_AnyRun_CarriesMemoryAndNoWayToReadAnything`,
`AgentComposition_BuildRecallInstructions_AnyRun_ForbidsAnsweringFromAnythingElse`

Asserts the recall turn carries only the memory family — no reader, lister, outline, plan, or
delegation — and its instructions forbid answering from anything but recalled memories.

#### AgentKitSamples-ResearchAssistant-RecallTurnSharesTheFiledStore: The Recall Turn Shares the Store

**Test**: `AgentComposition_BuildRecallTools_AnyRun_RecallsWhatTheRootCompositionFiled`

Files a memory through the root composition and asserts the recall turn's own tool set finds it.

#### AgentKitSamples-ResearchAssistant-SuppliesTheEmbeddingBackend: The Sample Supplies the Backend

**Test**: `AgentComposition_CreateEmbeddingGenerator_LocalBackend_UsesTheOfflineGenerator`

Resolves the backend for an offline run and asserts the sample's own offline generator is used.

#### AgentKitSamples-ResearchAssistant-CommandLineRequiresCorpus: A Corpus Is Required

**Tests**: `CommandLineOptions_Parse_NoCorpus_ThrowsCommandLineException`,
`CommandLineOptions_Parse_FlagWithoutValue_ThrowsCommandLineException`

Asserts a run with no corpus and a flag left without its value each raise a command-line exception
naming the offending flag.

#### AgentKitSamples-ResearchAssistant-CommandLineRejectsUnknownInput: Unknown Input Is Refused

**Tests**: `CommandLineOptions_Parse_UnknownArgument_ThrowsCommandLineException`,
`CommandLineOptions_Parse_UnknownEmbeddingBackend_ThrowsCommandLineException`

Asserts an unknown argument and an unknown embedding backend are each named rather than accepted.

#### AgentKitSamples-ResearchAssistant-CommandLineDefaultsToAnOfflineRun: The Default Run Is Offline

**Test**: `CommandLineOptions_Parse_OnlyCorpus_DefaultsToOfflineEmbeddingsAndDelegation`

Asserts a corpus-only command line defaults to the offline backend, the Copilot provider, delegation
on, and no prompts.

#### AgentKitSamples-ResearchAssistant-CommandLineAccumulatesPrompts: Prompts Accumulate In Order

**Test**: `CommandLineOptions_Parse_RepeatedPrompts_AreKeptInOrder`

Asserts two prompts are kept in the stated order.

#### AgentKitSamples-ResearchAssistant-CommandLineKeepsRecallQuestionApart: The Recall Question Is Kept Apart

**Tests**: `CommandLineOptions_Parse_RecallQuestion_IsKeptApartFromThePrompts`,
`CommandLineOptions_Parse_NoRecallQuestion_LeavesItUnset`

Asserts a recall question is kept separate from the prompts and is left unset when absent.

#### AgentKitSamples-ResearchAssistant-CommandLineResolvesCredentialWithoutPrinting: A Credential Is Resolved Safely

**Tests**: `CommandLineOptions_ResolveGitHubToken_ExplicitToken_WinsOverTheEnvironment`,
`CommandLineOptions_ResolveGitHubToken_BlankToken_IsNotTreatedAsACredential`

Asserts an explicit token wins over the environment and a blank token is not treated as a credential.

#### AgentKitSamples-ResearchAssistant-CommandLineDescribesTheRunHonestly: The Run Is Described Honestly

**Tests**: `CommandLineOptions_HelpText_AnyRun_DescribesTheEmbeddingChoice`,
`CommandLineOptions_Parse_HelpWithoutCorpus_RequestsHelpRatherThanFailing`,
`CommandLineOptions_DescribeModel_OllamaWithoutModel_NamesTheSamplesOwnDefault`,
`CommandLineOptions_DescribeModel_CopilotWithoutModel_SaysItCannotBeKnown`,
`CommandLineOptions_DescribeModel_ModelStated_ReportsItExactly`

Asserts the help text describes the embedding choice and the other flags, help short-circuits
validation, an Ollama run names the sample's own default model, an unnamed Copilot model is reported
as unknown with the remedy named, and a stated model is reported exactly.

#### AgentKitSamples-ResearchAssistant-DescriptorPhrasingDecidesConflictDetection: Phrasing Decides Detection

**Tests**: `MemoryFile_SubjectOnlyDescriptors_RefuseTheContradictingFact`,
`MemoryFile_SourceQualifiedDescriptors_StoreBothContradictingFacts`,
`LexicalEmbeddingGenerator_ConflictingFacts_ScoreByPhrasingNotByContradiction`

Files the same two contradicting facts under both phrasings and asserts the subject-only pair is
refused as a near-duplicate at similarity 1.0 while the source-qualified pair is stored, with the
threshold difference asserted directly.

#### AgentKitSamples-ResearchAssistant-OfflineEmbeddingsAreStableAndUnit: The Generator Is Stable and Unit-Length

**Tests**: `LexicalEmbeddingGenerator_GenerateAsync_SameText_ProducesTheSameVector`,
`LexicalEmbeddingGenerator_GenerateAsync_AnyText_ProducesAUnitVector`,
`LexicalEmbeddingGenerator_GenerateAsync_EmptyText_ProducesADefinedVector`,
`LexicalEmbeddingGenerator_GenerateAsync_SameWordsInAnyOrder_ScoresExactlyOne`,
`LexicalEmbeddingGenerator_GenerateAsync_SeveralTexts_AnswersInInputOrder`

Asserts the same text embeds to the same vector, any text embeds to a unit vector, empty or
punctuation-only text still embeds to a defined vector, the same words in any order score exactly one,
and a batch is answered in input order.

#### AgentKitSamples-ResearchAssistant-OfflineEmbeddingsRankByWordingNotMeaning: The Generator Is Lexical

**Tests**: `LexicalEmbeddingGenerator_GenerateAsync_RestatedFact_ScoresAboveTheDefaultThreshold`,
`LexicalEmbeddingGenerator_GenerateAsync_ParaphrasedFact_ScoresLowBecauseItIsLexical`,
`LexicalEmbeddingGenerator_GenerateAsync_DifferingNumeral_ScoresByTokenCountAlone`

Asserts a restatement scores above the published default threshold, a paraphrase scores below it, and
a differing numeral is caught only when the descriptor is long enough — the limitation a reader must
understand before copying the generator.
