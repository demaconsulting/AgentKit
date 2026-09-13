### TextFileLineBuffers

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileLineBuffers` class is an internal modeled unit that stores named text slots for
`text_file_cut_lines`, `text_file_copy_lines` and `text_file_paste_lines`.

#### Purpose

To make every line-range cut recoverable, and every copy possible, by holding the exact raw text a
cut removes from a file or a copy duplicates from it until a paste tool inserts it. The store is not a
user-facing tool and does not grant access to files. It is
shared state inside one text-file tool composition.

A later capture into the same slot replaces what was there, and paste reads without consuming. There
is deliberately no peek or clear operation: the store is a short-lived staging area for moves, not a
durable document store.

#### Data Model

The class is sealed and internal. One instance is allocated by `TextFilePack.CreateTools` for each
composition and shared by that composition's cut, copy and paste tools.

| Member        | Type                         | Invariant                                                |
| ------------- | ---------------------------- | -------------------------------------------------------- |
| `DefaultSlot` | `string`                     | `default`; used when cut, copy or paste supplies no name |
| `_gate`       | `object`                     | Lock guarding all access to `_slots`                     |
| `_slots`      | `Dictionary<string, string>` | Ordinal names; each value is raw captured text           |

Slot names must be non-null and non-empty. Captured text must be non-null; empty text is permitted,
although the cut tool normally captures at least one line.

#### Key Methods

##### Capture(string name, string text)

Stores `text` in `name`, replacing any previous value.

**Preconditions:** `name` is non-null and non-empty; `text` is non-null.

**Algorithm:** validates the inputs, takes `_gate`, and assigns `_slots[name] = text`.

**Postconditions:** a later `TryPaste` for the same name returns the most recently captured text.

##### TryPaste(string name, out string? text)

Retrieves the text in `name` without removing it.

**Preconditions:** `name` is non-null and non-empty.

**Algorithm:** validates the name, takes `_gate`, and calls `_slots.TryGetValue`.

**Postconditions:** on success, `text` is the captured raw text and the slot remains present. On
failure, `text` is null and the caller can refuse an empty slot.

##### PopulatedSlots()

Lists the names of the slots that currently hold captured text, in ordinal order, without removing
any.

**Preconditions:** none.

**Algorithm:** takes `_gate`, copies the slot names, sorts them ordinally, and returns them.

**Postconditions:** the returned list names exactly the currently populated slots and nothing else —
no file paths and no captured content — so a paste refusal can report which slots hold content
using only the names the model itself chose. It is internal refusal guidance, not a published tool.

#### Error Handling

Null or empty slot names, and null captured text, are programming errors from the tool units and are
reported by argument exceptions. Model-facing validation happens in the cut, copy and paste tools
before this unit is called.

The lock makes concurrent calls safe. No file-system failures occur here because the unit stores only
in-memory text.

#### Dependencies

The unit depends only on Base Class Library collections and locking. It is referenced by
`TextFilePack`, `TextFileCutLinesTool`, `TextFileCopyLinesTool`, and `TextFilePasteLinesTool`.

#### Callers

`TextFilePack.CreateTools` constructs one instance per composition. `TextFileCutLinesTool` and
`TextFileCopyLinesTool` call `Capture`, and `TextFilePasteLinesTool` calls `TryPaste` and, when a
requested slot is empty, `PopulatedSlots` to name the slots that do hold content.
