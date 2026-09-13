### MemoryPack

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryPack` class is the public modeled unit that publishes the memory tool family as one pack.

#### Purpose

To be the single thing an application attaches, and the single place the application's three
decisions — which embedding backend, which controls, which persistence — enter the family.

#### Data Model

The class is sealed and immutable after construction.

| Member       | Type                  | Invariant                                  |
| ------------ | --------------------- | ------------------------------------------ |
| `_generator` | `IEmbeddingGenerator` | Non-null; never inspected                  |
| `_options`   | `MemoryOptions`       | Non-null; supplied controls or the default |
| `_store`     | `IMemoryStore?`       | Null when the application supplied none    |

Two constants are published: `FamilyPrefix`, `memory`, and `SuggestedInstruction`, the instruction
an application should give an agent that carries the family. That instruction states three things a
model will not supply on its own: file as you read, recall before answering, and correct a fact
drawn from a *different* document with `memory_revise` citing that document — reserving
`memory_update` for a correction from the source a memory already cites. The third is there because
a live model repeatedly chose the update tool for a changed-source correction, leaving the memory
citing a superseded document; the subsystem design carries the observation and the reason this is
instruction rather than tool behavior. No adherence figure is claimed for the wording. `Options` is
exposed as a property so
an application can report the configuration it is running under — the threshold in particular is the
number that explains why a memory was not stored — without keeping a second copy that could
disagree.

`_store` is held as null rather than as an eagerly created default. The difference between "the
author's store, shared" and "a fresh store per composition" is exactly whether the author supplied
one, and collapsing the two here would silently share a default store between a parent agent and its
children.

The pack does not own the generator or the store and disposes of neither: both may outlive it and
both may be shared with the rest of the application.

#### Key Methods

##### MemoryPack constructor

Constructs the pack, taking `embeddingGenerator`, an optional `options` and an optional `store`.

**Preconditions:** `embeddingGenerator` is non-null. The other two are optional.

**Algorithm:** validates the generator, substitutes `MemoryOptions.Default` for absent options, and
holds the store as supplied.

**Postconditions:** the shortest useful composition is `new MemoryPack(embeddings)` — default
controls, and memories that live as long as the agent does. Requiring the generator is what makes a
host-capability flag unnecessary: an application that cannot embed cannot construct the pack at all.

##### RequiredCapabilities

**Postconditions:** `HostCapabilities.None`, so every composition receives the family rather than
gating it behind a declaration an application would have to know to make.

##### CreateTools(PathPolicy policy)

Creates the family's tools over the store this composition uses.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates the policy, resolves the store as `_store ?? new InMemoryMemoryStore()`,
and constructs the five tools over it through their internal factories.

**Postconditions:** five tools in the fixed order `memory_file`, `memory_recall`, `memory_update`,
`memory_revise`, `memory_forget`. The order is a contract rather than an incidental collection
order, because the order a model sees the tools in is observable in tool selection; it is the order
of the work — write something down, find it again, correct what it says, change what it is about,
and finally drop it.

When no store was supplied, a fresh `InMemoryMemoryStore` is allocated here as a local, never a
field, on every call. That is what binds a default set of memories to one agent: two compositions
never share a default store, and a delegated agent — whose tools are created by a separate call —
therefore keeps its own. This is the analogue of how the Todo subsystem allocates one task list per
composition. When a store was supplied, that store is used every time, because sharing or persisting
memories is precisely the thing an author supplies a store in order to do.

The policy is accepted and ignored: this family touches no files, so it has nothing to judge against
a policy. It is still validated, so that a composing application that forgot one is told at the
point it forgot rather than by a sibling family later.

#### Error Handling

A null embedding generator and a null policy are programming errors in the composing application and
are reported by `ArgumentNullException`. The pack raises nothing at tool-call time; every
model-facing refusal belongs to the tool units.

#### Dependencies

`MemoryOptions`, `IMemoryStore`, `InMemoryMemoryStore`, all five tool units, AgentKitCore's
`IToolPack`, `HostCapabilities`, `PathPolicy` and `ToolPackBuilder` contract, and
`IEmbeddingGenerator` from `Microsoft.Extensions.AI.Abstractions`.

#### Callers

An application attaches the pack through `ToolPackBuilder.Add`, which calls `CreateTools` once per
`Build`. An `AgentPack` composing a delegated agent calls `CreateTools` again for that child, which
is what gives the child its own memories unless the application deliberately supplied a shared
store.
