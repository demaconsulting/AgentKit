## CompactingAgentSession

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `CompactingAgentSession` class is the session that keeps its own transcript, watches the window,
and rotates into a fresh provider session when the context fills.

### Purpose

This is where the parts meet. The options say what the agent is and when to compact; the layout
accounts for the context; the rotation engine ages it; the provider-session factory produces the
replacement. This class owns the sequencing and nothing else, which is what keeps every other part
independently testable.

**Rotation is a replacement, not an edit.** When the conversation crosses the rotation threshold, the
engine consolidates older history, a new provider session is created and adopted seeded from the
preserved content, and only then is the session it replaced disposed. That is the only reduction
both provider shapes support, and it is why the behavior is identical on either.

**The transcript is kept here, out of session.** Consolidation is a separate stateless call that
receives the material as input, never a request to the live session to summarize itself: doing that
spends the session's own context on the summary and provokes the provider's built-in compactor.

### Data Model

Public properties:

- **`Layout`** (`ContextLayout`) — The engine's own account of the context. Replaced wholesale on every turn; the
  instance read is a snapshot
- **`Usage`** (`ContextUsage`) — The usage after the most recent turn, marked with where it came from
- **`RotationCount`** (`int`) — How many times the provider session has been replaced
- **`ConsolidationCount`** (`int`) — The total consolidations every rotation of this session has performed

Private state: the options, the provider-session factory, the live provider session (replaced at
every rotation), a disposal flag, a release flag recording whether the live provider session has
actually been released, and the fixed overhead the first provider session folded into its own
conversation count instead of breaking out, measured once at creation against the empty conversation
the session starts from.

`ConsolidationCount` is exposed because summarizer calls are the dominant cost of this arrangement,
and the tiered scheme's advantage over a flat rolling summary is partly that it makes fewer of them.
Exposing the running total lets an application see that cost rather than infer it.

**Instances are not safe for concurrent use**, consistent with `IAgentSession`: one session is one
conversation, and turns within a conversation are sequential by nature.

### Key Methods

#### CreateAsync(AgentSessionOptions options, IProviderSessionFactory providerSessionFactory, CancellationToken cancellationToken)

Creates the session and its first provider session. The first provider session is seeded with no
history, because there is none yet; the instructions and tools it carries are the same ones every
later rotation will carry.

A static asynchronous factory rather than a constructor, because creating the first provider session
is asynchronous and a constructor cannot await. It is also the one moment the conversation is
**empty by construction**, so whatever a provider-reported usage figure attributes to the
conversation here is fixed overhead it did not break out; that figure is measured once and credited
to the convergence bound thereafter. A provider that reports its window from the outset
is checked here, and a session whose provider reports a window it could not converge in is refused
with the provider's release attempted; see *SendAsync* below for why.

**Throws:** `ArgumentNullException` for a null options or factory; `InvalidOperationException` when
the factory returns null or the created session reports a window the session could not converge in;
`OperationCanceledException` on cancellation.

#### SendAsync(string message, CancellationToken cancellationToken)

**Algorithm:**

1. Reject use after disposal, and reject a blank message.
2. Take the turn against the live provider session.
3. **Once the provider has accepted it**, append the outgoing message and everything the turn
   produced to the transcript, in that order and in a single append.
4. Read usage: the live session's own account if it reports one, otherwise an estimate from the
   layout against the configured window.
5. Refuse a reported window in which a rotated context could not land below the rotation threshold,
   releasing the live provider first.
6. Compare the usage figure's own conversation tokens against the rotation threshold derived from the
   window that usage figure was measured against: the provider's reported window, less the overhead
   that provider itself reported, when the provider reported one; the configured window, less the
   estimated fixed overhead, otherwise.
7. Below the threshold, return the answer with `RotationOccurred` false.
8. At or above it, rotate, and return the answer with `RotationOccurred` true and any saturation the
   rotation reported.

**Why conversation tokens rather than raw usage, and why they are not computed here.** The threshold
is a fraction of the effective window, so the comparison must exclude the fixed overhead. The
exclusion is performed by whoever produced the usage figure, not by this class: `ContextUsage`
carries the conversation count, and this class reads it. Computing it here — by subtracting
`AgentSessionOptions.FixedOverheadTokens` from the usage, which is what this class used to do — mixed
two currencies whenever the provider reported. The usage and the window were then real tokens from
the provider's own tokenizer and the subtrahend was this library's four-characters-per-token
estimate of the system prompt and the tool declarations, so the difference was a figure in neither,
and every threshold comparison downstream inherited the error. Tool declarations are nested JSON
schemas, which is the material that ratio serves worst: the compaction spike estimated eleven tools
at 2,589 tokens, and the real count for such a set can differ substantially. Reading a carried
conversation count keeps each path within one currency — reported throughout, or estimated
throughout.

**Why the threshold follows the window the usage came from.** The two numbers must describe the same
window or the comparison means nothing. The guarantee rotation exists to deliver — rotating early
enough that the provider's own compactor never fires — is a claim about the window the provider
actually has, so a provider that reports one overrides the configured figure. A host configuring
128,000 tokens against a provider reporting 32,000 would otherwise be allowed four times past the
provider's own firing point, which is the failure this package exists to prevent; the reverse
mismatch would rotate long before it needed to, spending summarizer tokens and prompt cache for
nothing. Both paths apply the same arithmetic — remove the overhead, apply the policy's rotation
fraction, floor at one token — so they differ only in which window they measure and whose overhead
they remove from it. The reported path removes `ContextUsage.OverheadTokens`, which is the reported
total less the reported conversation and so is in the provider's own tokens; the estimate path keeps
the configured threshold, in which the estimated overhead was removed from the configured window the
estimate was measured against. A provider that reports totals but no split is credited no overhead
at all, which rotates earlier rather than later and so errs on the side the guarantee needs — a
statement about *this* comparison and no other; the convergence check below cannot inherit it.

**Why nothing is recorded until the provider accepts the turn.** A provider is entitled to honor
cancellation or fail before taking the turn — the in-memory provider does exactly that for a token
that was already canceled, and for one canceled while its responder was running. A message recorded
ahead of that would be a turn no provider ever saw,
which would survive in the transcript, be consolidated at the next rotation, and be seeded into the
replacement session as though it had happened. Recording after the call means a refused turn leaves
the session exactly as it was.

**Why the turn is recorded in one append rather than two.** A `SessionTranscript` is immutable and
copies its whole backing array on each append, so appending the message and then the turn's entries
copied a filling window twice per turn. That is quadratic in the number of entries between rotations
with double the constant, and a long conversation is the entire case this package exists for.
Collecting both halves into one sequence leaves the observable order identical, halves the copying,
and preserves the rule above: nothing at all is appended until the provider has returned.

**Why compaction happens after the answer.** The turn is served by the session that was live when it
arrived, and the replacement is prepared for the turn after. A caller therefore never waits on a
summarizer before receiving an answer the session could already give.

**Why a reported window a session could not converge in is refused rather than adapted.** A rotation
leaves the conversation holding at most the tier budgets plus the framing each seeded tier record
carries. The invariant the session needs is that this figure lands **below** the rotation threshold;
merely holding it is the necessary condition, not the sufficient one, and asserting only that was
the defect. For any window between the bound and the bound divided by the rotation fraction, the
guard passed, the session rotated, the rotated layout was still at or above the threshold, and it
rotated again on every following turn — spending a summarizer call and a provider session per turn
while raising no saturation signal, because each individual consolidation reduced perfectly
normally. Adapting was considered and rejected: shrinking the tier budgets to fit would silently
discard the compaction policy the host configured and would have to re-consolidate records already
written against larger budgets, which is precisely what an immutable layout exists to prevent;
ignoring the reported window would reinstate the defect the reported-window override removes,
letting the session run past the provider's own compactor. `AgentSessionOptions` already refuses a
*configured* window on the same test, through the shared `ConvergesAt` predicate, so failing here
keeps one rule — a window a session could not converge in is refused —
and differs only in when the figure becomes knowable. The check runs at creation and after every
turn, because a provider may only begin reporting, or report a smaller window, once it has answered
something. The live provider is released before the exception is thrown, since the session is being
abandoned mid-life and the caller has no handle to dispose; a failure to release is swallowed rather
than allowed to replace the configuration error the caller can act on, and the release flag stays
false so an explicit `DisposeAsync` still retries it. The release is unconditional: every entry point
holds a provider session that has not been released — `CreateAsync` a freshly created one,
`SendAsync` one already checked against disposal, and `RotateAsync` a replacement it has just
adopted — so a guard on the release flag asserted something already known.

**Why overhead a provider does not break out is credited to that bound.** `FromProvider` accepts
totals without a conversation split, and the figure it produces then reports zero overhead and calls
the whole of its usage conversation. That default is safe for the rotation trigger, which is a
comparison of an inflated conversation against a threshold taken from the whole window and so fires
early. It is not safe here, and *the same default being safe in one place and unsafe in the other is
how this was missed*: crediting no overhead makes the window look larger than it is, while the figure
a rotated context will actually report still carries the fold. A 500-token window with a 311-token
rotated bound passes a threshold of 350, and the replacement it then produces is reported at 411
against that same 350 and rotates on every turn thereafter. The fold is therefore **measured, not
assumed**. The first provider session is seeded with no history at all, so whatever a
provider-reported figure calls conversation at that instant is not conversation — it is the system
prompt, the tool declarations and whatever framing the provider charges for, in the provider's own
tokens — and the session records it once, at creation. It is added to the **bound**, which is the
side of the comparison the reported conversation figure sits on, rather than subtracted from the
window; subtracting it would discount it by the rotation fraction while the comparison pays for all
of it. The trigger is left exactly as it was, and the two are then consistent by construction: a
rotated context reports at most the bound plus the fold, and this check has established that the
threshold exceeds that sum. For a provider reporting the split the fold is zero and nothing changes,
and for an estimated figure the check does not run at all.

**What an adapter that genuinely cannot split its counts should do: nothing.** It keeps reporting
totals alone. Requiring a split would either force it to fabricate one — indistinguishable from a
measurement at the point it is consumed, which is the defect `ContextUsage` exists to remove — or
push an otherwise sound adapter onto the estimating path, where this library's character ratio would
decide when a real provider rotates. The one obligation such an adapter carries is to report from the
moment the session exists rather than only once it has answered something, because an empty
conversation is the only moment the fold is separable; an adapter that begins reporting later is
credited only what it breaks out, exactly as before.

**Why the failure says the release was *attempted*.** The catch above deliberately leaves the
release flag false so a later call can retry, and the message nonetheless claimed the session "has
been released" — untrue in exactly the case it was reporting. On the `CreateAsync` path no session
handle is returned at all, so an operator reading that claim has nothing left to retry the release
with and no reason to suspect the provider still holds a session. The message therefore states what
was attempted and that a failed attempt needs retrying, which is true on every path it is thrown
from. This package's rule is that a diagnostic states facts.

The window this check measures is the reported window less the overhead the provider itself
reported, so that subtraction is in one currency. The bound it is compared against — the tier budgets
and their seed framing — is not: those are configured and enforced in estimated tokens, because that
is the only currency a host can express them in before a provider has said anything. The check is
therefore an approximate comparison and is not claimed to be more, which is part of what the
rotation fraction's thirty percent of unspent window is for. What it no longer does is corrupt the
reported window itself before making the comparison.

**Throws:** `ArgumentException` for a blank message; `ObjectDisposedException` once disposed;
`InvalidOperationException` when the live provider — or a replacement a rotation adopted — reports a
window the session could not converge in; `OperationCanceledException` on cancellation.

#### RotateAsync(CancellationToken cancellationToken)

Ages the context by one rotation and replaces the live provider session with one seeded from the
result.

**Algorithm:** consolidate through `RotationEngine`, telling it which currency the crossing was
measured in — the `Origin` of this turn's usage figure; if it consolidated nothing, abandon the rotation
and report that none occurred; otherwise build a seed from the new layout; create the replacement;
adopt it — swapping the provider reference and updating the layout, the rotation count, the
consolidation total and the usage in one step containing no `await`; dispose the one it replaced;
validate the replacement's reported window; return that a rotation occurred, with the saturation
reports.

**Why the trigger's currency is handed to the engine.** The threshold comparison above is made in
whichever currency this turn's usage carries, while the engine's split is measured in estimated
tokens throughout. When a provider reported the crossing the two disagree by construction, and an
engine left to assume otherwise consolidated nothing, seeded no replacement, and reported the turn
as ordinary — leaving the provider to run into its own compactor with both halves behaving exactly
as documented in their own currency. Passing `ContextUsage.Origin` makes a provider-reported
crossing force a real consolidation; see *RotationEngine Unit Design*.

**Why the replacement is validated before the rotation is reported as successful.** Nothing obliges
a factory to return a session like the one it replaced — a routed deployment, a changed model or a
downgraded tier all report a smaller window — and the adoption reads the replacement's usage, so the
window is knowable at that moment. It was read and not checked, so a session this library could
never converge in was adopted, the turn was answered normally, and the caller was told the rotation
had succeeded; the replacement was found unusable only on the turn after, once a message had
already been sent into it. `EnsureReportedWindowConvergesAsync` is therefore run against it here,
which releases the replacement and abandons the session rather than returning. It is placed **after**
the superseded session has been disposed, so abandoning the rotation over an unusable replacement
does not also leak the session it replaced. That release can itself fail, like any other, which is
why the message it throws claims only that release was attempted.

**Why a rotation that consolidated nothing is abandoned.** The engine returns the layout unchanged
when the transcript already fits tier zero and the crossing was this library's own estimate, and for
a pure function over a layout that genuinely
costs nothing. It is not free here: carrying it out creates a replacement provider session, disposes
the live one, increments the rotation count, and tells the caller a rotation happened — all to
arrive at exactly the context the session already had. Repeated every turn that is a provider
session per turn spent to achieve nothing, and it is invisible, because no consolidation ran and so
nothing could saturate. Measured before this was fixed, a policy of `[100, 1]` in a 128-token window
— accepted by both guards as they then stood — did that on 16 of 20 turns, creating 17 provider
sessions while reporting no saturation at any point.

The convergence invariant makes this case unreachable for a layout sitting within its tier budgets:
the threshold it guarantees exceeds the coarse tiers and their framing by more than tier zero's
budget, so anything able to cross the threshold must overflow tier zero. It remains reachable for a
layout whose tier is over budget — precisely the saturated case the library exists to report — so
the guard is kept rather than argued away. A provider-reported crossing reaches it only when there
is no verbatim history at all to consolidate, because the engine forces a consolidation otherwise.

**The order is deliberate: consolidate first, create the replacement second, adopt it third, dispose
the old session last.** A summarizer failure therefore leaves the session exactly as it was, still
able to answer, rather than leaving it with no provider session at all. Disposing only once the
replacement exists is also what makes the swap atomic from a caller's point of view.

**Why the adoption precedes the disposal, and carries no await.** Updating the state after awaiting
the disposal is not exception-safe: an adapter whose `DisposeAsync` fails leaves this session
pointing at the replacement while its layout and counters still describe the session it replaced, so
the next turn appends to — and may rotate — the wrong transcript. Performing the whole transition
before that await, with nothing to suspend on in the middle, means the provider reference, the
layout and the counters describe the same session at every point an exception could be observed.

**Why a failed disposal does not fail the rotation.** By the time the superseded session is
disposed, the rotation has already succeeded: the context was consolidated, the replacement was
created, and this session is coherent against it. The exception is caught and not rethrown, because
throwing it on would report the opposite to the caller and leave it holding a session it would
reasonably believe to be broken. The cost of an adapter that cannot release its session is a
provider-side session that outlives its use — the adapter's own defect, which discarding a good
session on top of it does not repair. Explicit `DisposeAsync` on this session is a different matter
and does propagate: a caller that asked for the session to be released is entitled to learn that it
was not.

#### DisposeAsync()

Disposes whichever provider session is currently live. Sessions replaced by earlier rotations were
already disposed at the moment they were replaced, so nothing is left holding server-side state.

A failure to release propagates — the deliberate opposite of a rotation, which swallows the same
failure because by then it has already succeeded. **Because it propagates, disposal stays
retryable.** The disposal flag and the release flag are separate, and they answer different
questions: whether this session may still be used, and whether anything is still held on the
provider's side. The first is set from the first call, so the session refuses further turns whether
or not the release succeeded; the second is set only once the provider's own disposal has completed.
A later call therefore attempts the release again rather than returning as though it had happened,
so a transient provider failure does not become a permanent leak. Marking the session released
before awaiting the release would make that impossible: every later call would return at the flag
while the provider still held the conversation.

Once the release has succeeded, disposing again is permitted and does nothing, because a disposal
pattern that threw on a second call would make defensive cleanup harder than leaving the resource
open.

#### ReadUsage(IProviderSession provider, AgentSessionOptions options, ContextLayout layout)

Static, because it depends on nothing but its arguments. Returns the provider's own figures when it
implements `IContextUsageReporter` and reports something, and an estimate from the layout otherwise.
Keeping the preference rule as a single visible decision, rather than scattering it across the call
sites that need a usage figure, is what keeps the two provider families on one code path.

### Error Handling

- **Null options or provider factory** — `ArgumentNullException` propagates
- **Provider factory returns null** — `InvalidOperationException` propagates
- **Blank message** — `ArgumentException` propagates
- **Provider reports a window the session could not converge in** — `InvalidOperationException`
  propagates, stating the release was attempted; the live provider's release is attempted first and
  the session refuses further turns. This applies to a replacement adopted by a rotation as much as
  to the session a turn was taken against
- **Release fails while refusing an unusable window** — Caught and not reported; the configuration
  error is the one the caller can act on, the release flag stays false so the release remains
  retryable, and the message claims only that release was attempted
- **Use after disposal** — `ObjectDisposedException` propagates
- **Summarizer fails during rotation** — Propagates; the session is left intact and still able to answer
- **Superseded provider session fails to dispose during rotation** — Caught and not reported; the
  rotation already succeeded and the session is coherent against its replacement
- **Live provider session fails to dispose on `DisposeAsync`** — Propagates; a caller that asked for
  the session to be released is entitled to learn that it was not, and the release may be retried by
  disposing again
- **Rotation reports saturation** — Surfaced on the response; not an exception
- **Second disposal** — Permitted; releases the provider session if an earlier attempt failed,
  otherwise does nothing

### Dependencies

- **AgentSession** — implements `IAgentSession` and returns `AgentSessionResponse`; see
  *AgentSession Unit Design*.
- **AgentSessionOptions** — the configuration and every derived figure; see *AgentSessionOptions
  Unit Design*.
- **ContextLayout** — the state a rotation acts on, and the seed builder; see *ContextLayout Unit
  Design*.
- **SessionTranscript** — the append-only record of each turn; see *SessionTranscript Unit Design*.
- **RotationEngine** — performs the rotation; see *RotationEngine Unit Design*.
- **ProviderSession** — the seed, the session and the factory; see *ProviderSession Unit Design*.
- **ContextUsage** — the usage figure and the optional reporting contract; see *ContextUsage Unit
  Design*.

### Callers

An application calls `CreateAsync` once and then `SendAsync` per turn, holding the result as an
`IAgentSession`. Within this system nothing else calls it.
