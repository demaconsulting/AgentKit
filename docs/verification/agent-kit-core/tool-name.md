## ToolName Unit Verification Design

This document describes the unit-level verification strategy for the `ToolName` class.

### Verification Approach

`ToolName` is verified through unit tests that call `Create` and `Validate` directly. There are
no dependencies to substitute — the unit depends only on the base class library.

Every failing scenario asserts an **exception** rather than a returned result, because a
malformed tool name is a construction-time programming error discovered by the developer who
wrote it. The non-throwing discipline this library observes elsewhere applies to runtime policy
refusals, not to argument validation; see _ToolName Unit Design_.

The reserved-name scenario asserts on the **message text**, not merely on the exception type, so
that the rule cannot be quietly folded into the generic family-prefix rule without the loss being
visible. That rule is redundant against the family-prefix rule today, and the scenario is what
preserves it against a future relaxation.

Unit tests reside in `ToolNameTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: None
- **Isolation**: Each test validates its own name; no state is shared

### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or exception beyond
those explicitly asserted. Any malformed name that is accepted, any conforming name that is
refused, and any reserved name accepted or refused for the wrong reason constitutes a failure.

### Test Scenarios

#### AgentKitCore-ToolName-Compose: A Family and a Verb Compose Into a Name

**Test**: `ToolName_Create_FamilyAndVerb_ProducesUnderscoreSeparatedName`

Normal operation: the family, a single underscore, then the verb.

#### AgentKitCore-ToolName-Compose: A Multi-Word Family Composes Correctly

**Test**: `ToolName_Create_MultiWordFamily_ProducesUnderscoreSeparatedName`

Asserts the internal underscore of a multi-word family is preserved, confirming the family prefix
is the leading portion of a name rather than the single token before the first underscore.

#### AgentKitCore-ToolName-Compose: Composing Without a Family Is Refused

**Test**: `ToolName_Create_EmptyFamily_ThrowsArgumentException`

Error path: an empty family would compose to a leading underscore.

#### AgentKitCore-ToolName-Compose: Composing Without a Verb Is Refused

**Test**: `ToolName_Create_EmptyVerb_ThrowsArgumentException`

Error path: an empty verb would compose to a trailing underscore.

#### AgentKitCore-ToolName-CharacterSet: A Conforming Name Is Accepted

**Test**: `ToolName_Validate_ValidName_DoesNotThrow`

Normal operation, and the scenario that stops the ruleset becoming uniformly rejecting: lowercase
letters, digits and single underscores are all permitted.

#### AgentKitCore-ToolName-CharacterSet: An Uppercase Character Is Refused

**Test**: `ToolName_Validate_UppercaseCharacter_ThrowsArgumentException`

Asserts uppercase is excluded everywhere, not only in the first position. Providers treat names
case-sensitively, so a mixed-case name is a coin-flip on how a prompt refers to it.

#### AgentKitCore-ToolName-CharacterSet: An Unsupported Character Is Refused

**Test**: `ToolName_Validate_UnsupportedCharacter_ThrowsArgumentException`

Boundary condition: the hyphen is refused even though providers accept it, because mixing
separators makes the family prefix ambiguous.

#### AgentKitCore-ToolName-CharacterSet: A Leading Digit Is Refused

**Test**: `ToolName_Validate_LeadingDigit_ThrowsArgumentException`

Asserts the first character must be a lowercase letter.

#### AgentKitCore-ToolName-CharacterSet: A Name Beyond the Length Ceiling Is Refused

**Test**: `ToolName_Validate_NameExceedingMaximumLength_ThrowsArgumentException`

Boundary condition: the scenario first asserts the constructed name is exactly one character
beyond the ceiling, so it cannot silently become a test of some other rule.

#### AgentKitCore-ToolName-FamilyPrefixRequired: A Name Without an Underscore Is Refused

**Test**: `ToolName_Validate_NameWithoutUnderscore_ThrowsArgumentException`

Asserts a well-formed but bare name carries no family and is refused.

#### AgentKitCore-ToolName-FamilyPrefixRequired: A Leading Underscore Is Refused

**Test**: `ToolName_Validate_LeadingUnderscore_ThrowsArgumentException`

Asserts a leading underscore, which would name an empty family, is refused.

#### AgentKitCore-ToolName-FamilyPrefixRequired: A Trailing Underscore Is Refused

**Test**: `ToolName_Validate_TrailingUnderscore_ThrowsArgumentException`

Asserts a trailing underscore, which would name an empty verb, is refused.

#### AgentKitCore-ToolName-FamilyPrefixRequired: Consecutive Underscores Are Refused

**Test**: `ToolName_Validate_ConsecutiveUnderscores_ThrowsArgumentException`

Asserts a doubled separator, which makes the family prefix ambiguous, is refused.

#### AgentKitCore-ToolName-ReservedNamesRejected: A Bare Agent Framework Name Is Refused

**Test**: `ToolName_Validate_BareAgentFrameworkName_IsRejected`

A theory over `read`, `write` and `delete`. Asserts each is refused **and** that the message names
the collision with the Agent Framework's file access provider, so the rule cannot be folded into
the generic family-prefix rule without this scenario failing.

#### AgentKitCore-ToolName-RejectMissingName: A Null Name Is Refused

**Test**: `ToolName_Validate_NullName_ThrowsArgumentNullException`

Error path: a null name is a defect in the tool author's code.

#### AgentKitCore-ToolName-RejectMissingName: An Empty Name Is Refused

**Test**: `ToolName_Validate_EmptyName_ThrowsArgumentException`

Error path: an empty name gives the model nothing to invoke.
