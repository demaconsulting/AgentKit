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
engine consolidates older history, the live provider session is disposed, and a new one is created
seeded from the preserved content. That is the only reduction both provider shapes support, and it
is why the behavior is identical on either.

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
every rotation), a disposal flag, and a release flag recording whether the live provider session has
actually been released.

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
is asynchronous and a constructor cannot await.

**Throws:** `ArgumentNullException` for a null options or factory; `InvalidOperationException` when
the factory returns null; `OperationCanceledException` on cancellation.

#### SendAsync(string message, CancellationToken cancellationToken)

**Algorithm:**

1. Reject use after disposal, and reject a blank message.
2. Take the turn against the live provider session.
3. **Once the provider has accepted it**, append the outgoing message and everything the turn
   produced to the transcript, in that order.
4. Read usage: the live session's own account if it reports one, otherwise an estimate from the
   layout against the configured window.
5. Compute conversation tokens as usage less the options' fixed overhead, and compare against the
   rotation threshold derived from the window that usage figure was measured against: the provider's
   reported window when the provider reported one, the configured window otherwise.
6. Below the threshold, return the answer with `RotationOccurred` false.
7. At or above it, rotate, and return the answer with `RotationOccurred` true and any saturation the
   rotation reported.

**Why conversation tokens rather than raw usage.** The threshold is a fraction of the effective
window, so the comparison must exclude the fixed overhead. Subtracting it is what makes the
comparison mean the same thing whether the figure came from the provider or from the library's own
estimate.

**Why the threshold follows the window the usage came from.** The two numbers must describe the same
window or the comparison means nothing. The guarantee rotation exists to deliver — rotating early
enough that the provider's own compactor never fires — is a claim about the window the provider
actually has, so a provider that reports one overrides the configured figure. A host configuring
128,000 tokens against a provider reporting 32,000 would otherwise be allowed four times past the
provider's own firing point, which is the failure this package exists to prevent; the reverse
mismatch would rotate long before it needed to, spending summarizer tokens and prompt cache for
nothing. Both paths apply the same arithmetic — subtract the fixed overhead, apply the policy's
rotation fraction, floor at one token — so they differ only in which window they measure. The
estimate path keeps the configured threshold because the estimate was measured against the
configured window.

**Why nothing is recorded until the provider accepts the turn.** A provider is entitled to honor
cancellation or fail before taking the turn — the in-memory provider does exactly that for a token
that was already canceled. A message recorded ahead of that would be a turn no provider ever saw,
which would survive in the transcript, be consolidated at the next rotation, and be seeded into the
replacement session as though it had happened. Recording after the call means a refused turn leaves
the session exactly as it was.

**Why compaction happens after the answer.** The turn is served by the session that was live when it
arrived, and the replacement is prepared for the turn after. A caller therefore never waits on a
summarizer before receiving an answer the session could already give.

**Throws:** `ArgumentException` for a blank message; `ObjectDisposedException` once disposed;
`OperationCanceledException` on cancellation.

#### RotateAsync(CancellationToken cancellationToken)

Ages the context by one rotation and replaces the live provider session with one seeded from the
result.

**Algorithm:** consolidate through `RotationEngine`; build a seed from the new layout; create the
replacement; adopt it — swapping the provider reference and updating the layout, the rotation count,
the consolidation total and the usage in one step containing no `await`; dispose the one it
replaced; return the saturation reports.

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
  _AgentSession Unit Design_.
- **AgentSessionOptions** — the configuration and every derived figure; see _AgentSessionOptions
  Unit Design_.
- **ContextLayout** — the state a rotation acts on, and the seed builder; see _ContextLayout Unit
  Design_.
- **SessionTranscript** — the append-only record of each turn; see _SessionTranscript Unit Design_.
- **RotationEngine** — performs the rotation; see _RotationEngine Unit Design_.
- **ProviderSession** — the seed, the session and the factory; see _ProviderSession Unit Design_.
- **ContextUsage** — the usage figure and the optional reporting contract; see _ContextUsage Unit
  Design_.

### Callers

An application calls `CreateAsync` once and then `SendAsync` per turn, holding the result as an
`IAgentSession`. Within this system nothing else calls it.
