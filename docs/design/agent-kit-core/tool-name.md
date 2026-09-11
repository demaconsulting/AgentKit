## ToolName

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolName` class defines the tool naming convention and validates names against it.

### Purpose

A tool name is the only handle a model has on a tool, so it has to be unambiguous, stable, and
unique across every library an application combines. `ToolName` exists so that the convention is
stated once, in code, rather than being a note in a document each pack author may or may not have
read.

The convention is `{family}_{verb}`: a family prefix, a single underscore, and the remainder.
The family prefix is the load-bearing part — it is what keeps one library's tools from colliding
with another's when an application attaches several packs at once.

The class is static and stateless and is therefore safe for concurrent use.

### Data Model

`ToolName` holds no instance state:

| Member          | Type              | Description                                               |
|-----------------|-------------------|-----------------------------------------------------------|
| `MaxLength`     | `const int`       | The greatest number of characters a name may contain, 64. |
| `ReservedNames` | `static string[]` | The bare names `read`, `write` and `delete`.              |

`MaxLength` is not an arbitrary round number: the major model providers constrain function names
to `^[a-zA-Z0-9_-]{1,64}$`, so a name this library accepts is guaranteed to be a name a provider
accepts.

`ReservedNames` holds the bare tool names published by the Agent Framework's `FileAccessProvider`.

### Key Methods

#### Create(string family, string verb)

Composes a tool name from a family and a verb.

**Preconditions:** both `family` and `verb` are non-null and non-empty.

**Algorithm:** compose `family + "_" + verb`, pass the composed name through `Validate`, and
return it.

Composition and validation share the one code path deliberately, so the two can never diverge
into a name `Create` produces but `Validate` refuses.

**Throws:** `ArgumentNullException` / `ArgumentException` for a missing or empty part, and
whatever `Validate` reports for the composed name.

#### Validate(string name)

Applies the complete ruleset, in this order:

| #   | Rule                                                     | Failure                                        |
|-----|----------------------------------------------------------|------------------------------------------------|
| 1   | `name` is non-null and non-empty                         | `ArgumentNullException` or `ArgumentException` |
| 2   | `name` is not `read`, `write` or `delete` (ordinal)      | `ArgumentException`                            |
| 3   | `name.Length` is at most `MaxLength`                     | `ArgumentException`                            |
| 4   | the first character is a lowercase letter `a`–`z`        | `ArgumentException`                            |
| 5   | the last character is not `_`                            | `ArgumentException`                            |
| 6   | every character is `a`–`z`, `0`–`9` or `_`               | `ArgumentException`                            |
| 7   | no two consecutive `_`                                   | `ArgumentException`                            |
| 8   | at least one `_` is present                              | `ArgumentException`                            |

**The order matters**, because it determines which explanation a developer sees. The
reserved-name check comes first so that its explanation — a collision with the Agent Framework's
file access provider — is not displaced by the generic family-prefix message.

Consequences that follow from the ruleset, recorded here so nobody has to re-derive them:

- Rule 4 rejects uppercase in the first position and rule 6 rejects it everywhere else, so
  **names are entirely lowercase**. Model providers treat names case-sensitively, and a
  mixed-case name is a coin-flip on how a prompt refers to it.
- Rule 4 also rejects a leading `_` and a leading digit.
- Rules 5, 7 and 8 together guarantee a non-empty family prefix and a non-empty remainder.
- The hyphen is excluded from rule 6 even though providers accept it, because mixing separators
  makes the family prefix ambiguous.
- **Rule 2 is strictly redundant against rule 8 today** — none of the reserved names contains an
  underscore, so each would be rejected anyway. It is nevertheless written, tested and documented
  explicitly, because the point is the *explanation*, and because a future relaxation of rule 8
  must not silently reopen the collision.

### Error Handling

| Condition                        | Handling                                   |
|----------------------------------|--------------------------------------------|
| Null `name`, `family` or `verb`  | `ArgumentNullException` propagates         |
| Empty `name`, `family` or `verb` | `ArgumentException` propagates             |
| A name violating any rule        | `ArgumentException` propagates             |

Nothing is handled locally, and that is the correct choice here. **A malformed tool name is a
construction-time programming error** discovered by the developer who wrote it, not a runtime
policy decision reported to a model. The non-throwing discipline this library observes elsewhere
applies to *runtime policy refusals* — see *ToolResult Unit Design* — and the two must not be
confused: a refusal that throws strands an agent mid-turn, whereas a malformed name that returns
quietly ships a tool no model can reliably invoke.

### Design Constraints

**The reserved-name rule must not be folded into the family-prefix rule** on the grounds that it
is redundant. Its value is the message it produces and the protection it preserves if the prefix
rule is ever relaxed. The unit test asserts on the message text for exactly this reason.

**The character set is deliberately narrower than what providers accept.** Widening it to include
the hyphen would make a name's family prefix ambiguous, and widening it to include uppercase
would make a name's identity depend on how a prompt happened to spell it.

### Dependencies

N/A - `ToolName` depends only on the .NET base class library.

### Callers

`ToolName` is a public API entry point: a pack author calls `Create` to compose a name. Within
this system it is used by `GuardedToolFactory`, which validates every tool name before creating
the tool, and by `ToolResult.Denied`, which validates a redirect tool name.
