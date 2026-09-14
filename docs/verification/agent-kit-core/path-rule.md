## PathRule Unit Verification Design

This document describes the unit-level verification strategy for the `PathRule` class.

### Verification Approach

`PathRule` is verified through unit tests that construct grants directly and exercise the single
`Allows` containment decision. `RealPathResolver` is used as a real dependency rather than a
substitute, because a grant's confined location is resolved at construction and the correctness of
that resolution is part of the behavior being verified; the dependency is documented in _PathRule
Unit Design_.

Candidate locations are built from the grant's own `Root` property, which is the resolved location
it actually holds. This matches the documented contract — `Allows` takes a location that has already
been resolved — and keeps the tests independent of how the host's temporary directory is spelled.

The suite also verifies the permission-only part of the model. A `PathRule` carries an
`AccessLevel` of `ReadOnly` or `ReadWrite`, created through the `ReadOnly`, `ReadWrite`, and
`Unrestricted(AccessLevel, ...)` factories. That level never changes containment; it is surfaced so
`PathPolicy` can decide whether a grant authorizes a read only or both a read and a write, and so a
denial can describe a grant as read-only or read-write.

Unit tests reside in `PathRuleTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **File system**: A temporary directory tree created per test through `TempDirectoryFixture`
- **Mocking**: None; `RealPathResolver` is exercised as a real dependency
- **Isolation**: Each test constructs its own grant and its own fixture; no state is shared

### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or unexpected exception.
Any permitted location that should have been refused, any refused location that should have been
permitted, any access level not carried exactly as requested, any malformed grant accepted, or any
grant description that omits its location or access level constitutes a failure.

### Test Scenarios

#### AgentKitCore-PathRule-Unrestricted: An Unrestricted Grant Permits Any Location

**Test**: `PathRule_Allows_UnrestrictedGrant_AnyPath_ReturnsTrue`

Constructs an unrestricted read-only grant with no denied patterns and asserts that its `Root` is
absent and that an arbitrary resolved location is permitted.

#### AgentKitCore-PathRule-AccessLevel: An Unrestricted Grant Carries the Requested Access Level

**Test**: `PathRule_Unrestricted_CarriesRequestedAccessLevel`

A theory over `ReadOnly` and `ReadWrite`. Asserts an unrestricted grant carries exactly the level
requested and still has no location, proving the access level is permission only.

#### AgentKitCore-PathRule-Unrestricted: An Unrestricted Grant Still Honors Its Denied Patterns

**Test**: `PathRule_Allows_UnrestrictedGrantWithDenyPattern_MatchingPath_ReturnsFalse`

Constructs an unrestricted read-only grant excluding `*.key` and asserts that a matching resolved
location is refused and that the pattern is exposed. Verifies that wide access does not have to mean
unconditional access.

#### AgentKitCore-PathRule-AccessLevel: Rooted Factories Carry Their Access Levels

**Test**: `PathRule_RootedFactories_CarryTheirAccessLevel`

Constructs one grant with `PathRule.ReadOnly` and one with `PathRule.ReadWrite` over the same root
and asserts that each carries the level named by the factory that made it.

#### AgentKitCore-PathRule-Rooted: A Location Inside the Root Is Permitted

**Test**: `PathRule_Allows_RootedGrant_PathInsideRoot_ReturnsTrue`

Normal operation: a nested location beneath the grant's resolved root is permitted. The scenario is
about containment only, so the chosen access level does not affect the result.

#### AgentKitCore-PathRule-Rooted: The Root Itself Is Permitted

**Test**: `PathRule_Allows_RootedGrant_RootItself_ReturnsTrue`

Boundary condition: the root is part of the permitted location, not merely its edge, so that the
root can be listed.

#### AgentKitCore-PathRule-Rooted: A Location Outside the Root Is Refused

**Test**: `PathRule_Allows_RootedGrant_PathOutsideRoot_ReturnsFalse`

Error path: a location in the sibling directory is refused.

#### AgentKitCore-PathRule-Rooted: A Sibling Sharing the Root's Name Prefix Is Refused

**Test**: `PathRule_Allows_RootedGrant_SiblingWithSharedPrefix_ReturnsFalse`

Boundary condition guarding the separator in the containment test: a location beneath a directory
whose name merely begins with the root's name is refused. Such a neighbor is trivially created by an
attacker, so treating a shared prefix as containment would be an escape.

#### AgentKitCore-PathRule-DenyPatterns: A Denied Directory Name Refuses Its Whole Subtree

**Test**: `PathRule_Allows_DenyPatternMatchingDirectorySegment_ReturnsFalse`

A read-only grant excluding `.git` refuses a contained location inside that directory, verifying
that patterns apply to enclosing names and not only to the last one.

#### AgentKitCore-PathRule-DenyPatterns: A Denied File Name Is Refused Inside the Root

**Test**: `PathRule_Allows_DenyPatternMatchingFileName_ReturnsFalse`

A read-write grant excluding `*.key` refuses a contained location matching the pattern, verifying
that patterns override containment.

#### AgentKitCore-PathRule-RootNormalizedAtConstruction: A Root Spelled With Relative Segments Permits Its Contents

**Test**: `PathRule_Rooted_RootWithRelativeSegments_AllowsContainedPath`

Grants a location spelled through a redundant parent-directory detour. Asserts that the grant's
`Root` is the normalized location — proving normalization occurred — and that a location inside it
is permitted. Verifies that a location configured the way a person writes one remains usable.

#### AgentKitCore-PathRule-RejectInvalidRule: Null Root Throws ArgumentNullException

**Test**: `PathRule_ReadOnly_NullRoot_ThrowsArgumentNullException`

Asserts a grant with no location is refused at construction rather than silently behaving as
unrestricted.

#### AgentKitCore-PathRule-RejectInvalidRule: Empty Root Throws ArgumentException

**Test**: `PathRule_ReadWrite_EmptyRoot_ThrowsArgumentException`

The empty-string boundary for the location, distinct from the null case.

#### AgentKitCore-PathRule-RejectInvalidRule: Null Denied Pattern Throws ArgumentException

**Test**: `PathRule_ReadOnly_NullDenyPattern_ThrowsArgumentException`

Asserts a malformed pattern list is refused at construction, so a grant that looks tightened can
never in fact be looser than the operator believed.

#### AgentKitCore-PathRule-Describe: A Rooted Grant Names Its Location and Access Level

**Test**: `PathRule_Describe_RootedGrant_NamesLocationAndLevel`

Constructs a read-only grant and asserts `Describe()` includes the resolved root and the
`(read-only)` access label a denial will enumerate.

#### AgentKitCore-PathRule-Describe: An Unrestricted Grant Says Anywhere With Its Access Level

**Test**: `PathRule_Describe_UnrestrictedGrant_SaysAnywhere`

Constructs an unrestricted read-write grant and asserts `Describe()` returns
`anywhere (read-write)`, proving an unbounded grant still discloses its permission level.
