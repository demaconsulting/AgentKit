## PathRule Unit Verification Design

This document describes the unit-level verification strategy for the `PathRule` class.

### Verification Approach

`PathRule` is verified through unit tests that construct rules directly and exercise the single
`Allows` decision. `RealPathResolver` is used as a real dependency rather than a substitute,
because a rule's confined location is resolved at construction and the correctness of that
resolution is part of the behavior being verified; the dependency is documented in _PathRule Unit
Design_.

Candidate locations are built from the rule's own `Root` property, which is the resolved location
the rule actually holds. This matches the documented contract — `Allows` takes a location that has
already been resolved — and keeps the tests independent of whether the host's temporary directory
is itself reached through a link.

Unit tests reside in `PathRuleTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test through `ReparsePointFixture`;
  one scenario additionally creates a real directory link, using `cmd.exe /c mklink /J` on
  Windows and a directory symbolic link on Linux and macOS
- **Mocking**: None; `RealPathResolver` is exercised as a real dependency
- **Isolation**: Each test constructs its own rule and its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all twelve scenarios below pass without error or unexpected
exception. Any permitted location that should have been refused, any refused location that should
have been permitted, or any unexpected exception type constitutes a failure.

### Test Scenarios

#### AgentKitCore-PathRule-Unrestricted: An Unrestricted Rule Permits Any Location

**Test**: `PathRule_Allows_UnrestrictedRule_AnyPath_ReturnsTrue`

Constructs an unrestricted rule with no denied patterns and asserts that its `Root` is absent and
that an arbitrary location is permitted.

#### AgentKitCore-PathRule-Unrestricted: An Unrestricted Rule Still Honors Its Denied Patterns

**Test**: `PathRule_Allows_UnrestrictedRuleWithDenyPattern_MatchingPath_ReturnsFalse`

Constructs an unrestricted rule excluding `*.key` and asserts that a matching location is refused
and that the pattern is exposed. Verifies that wide access does not have to mean unconditional
access.

#### AgentKitCore-PathRule-Rooted: A Location Inside the Root Is Permitted

**Test**: `PathRule_Allows_RootedRule_PathInsideRoot_ReturnsTrue`

Normal operation: a nested location beneath the rule's root is permitted.

#### AgentKitCore-PathRule-Rooted: The Root Itself Is Permitted

**Test**: `PathRule_Allows_RootedRule_RootItself_ReturnsTrue`

Boundary condition: the root is part of the permitted location, not merely its edge, so that the
root can be listed.

#### AgentKitCore-PathRule-Rooted: A Location Outside the Root Is Refused

**Test**: `PathRule_Allows_RootedRule_PathOutsideRoot_ReturnsFalse`

Error path: a location in the sibling directory is refused.

#### AgentKitCore-PathRule-Rooted: A Sibling Sharing the Root's Name Prefix Is Refused

**Test**: `PathRule_Allows_RootedRule_SiblingWithSharedPrefix_ReturnsFalse`

Boundary condition guarding the separator in the containment test: a location beneath a directory
whose name merely begins with the root's name is refused. Such a neighbor is trivially created by
an attacker, so treating a shared prefix as containment would be an escape.

#### AgentKitCore-PathRule-DenyPatterns: A Denied Directory Name Refuses Its Whole Subtree

**Test**: `PathRule_Allows_DenyPatternMatchingDirectorySegment_ReturnsFalse`

A rooted rule excluding `.git` refuses a contained location inside that directory, verifying that
patterns apply to enclosing names and not only to the last one.

#### AgentKitCore-PathRule-DenyPatterns: A Denied File Name Is Refused Inside the Root

**Test**: `PathRule_Allows_DenyPatternMatchingFileName_ReturnsFalse`

A rooted rule excluding `*.key` refuses a contained location matching the pattern, verifying that
patterns override containment.

#### AgentKitCore-PathRule-RootResolvedAtConstruction: A Root Reached Through a Link Permits Its Contents

**Test**: `PathRule_Rooted_RootReachedThroughLink_AllowsContainedPath`

Creates a real directory link and roots a rule at the link. Asserts that the rule's `Root` differs
from the link's own path — proving resolution occurred — and that a location inside the link's
real target is permitted. Verifies that a legitimately linked working directory remains usable.

#### AgentKitCore-PathRule-RejectInvalidRule: Null Root Throws ArgumentNullException

**Test**: `PathRule_Rooted_NullRoot_ThrowsArgumentNullException`

Asserts a rule with no location is refused at construction rather than silently behaving as
unrestricted.

#### AgentKitCore-PathRule-RejectInvalidRule: Empty Root Throws ArgumentException

**Test**: `PathRule_Rooted_EmptyRoot_ThrowsArgumentException`

The empty-string boundary for the location, distinct from the null case.

#### AgentKitCore-PathRule-RejectInvalidRule: Null Denied Pattern Throws ArgumentException

**Test**: `PathRule_Rooted_NullDenyPattern_ThrowsArgumentException`

Asserts a malformed pattern list is refused at construction, so a rule that looks tightened can
never in fact be looser than the operator believed.
