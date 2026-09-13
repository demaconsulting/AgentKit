namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Ceilings a tool observes when reading, returning and attaching content.
/// </summary>
/// <remarks>
///     <para>
///     <b>The ceilings reason from the model's context budget, not from what is convenient in
///     bytes.</b> A tool's real constraint is not the file system or the network; it is that
///     everything a tool returns is spent out of the same finite context window the
///     conversation, the system prompt and the model's own reasoning must share. Choosing a
///     round number of bytes and hoping is how an agent ends up forgetting its instructions
///     halfway through a task — a failure that presents as the model becoming vague rather than
///     as an error anyone can see.
///     </para>
///     <para>
///     <see cref="DefaultMaxReadBytes"/> is 64 KiB. Common tokenizers encode ordinary English
///     prose and source code at roughly four bytes per token, so 64 KiB is on the order of
///     sixteen thousand tokens — generous for a source file or a chapter of a document, while
///     still leaving room for the conversation that has to follow.
///     </para>
///     <para>
///     <see cref="DefaultMaxResultCharacters"/> is 32,000. What a tool <em>reads</em> and what
///     it <em>returns</em> are deliberately different budgets, and the return budget is the
///     tighter one: a tool commonly reads a whole file and returns a region of it. Setting this
///     equal to <see cref="DefaultMaxReadBytes"/> would let one result crowd out every
///     subsequent step.
///     </para>
///     <para>
///     <see cref="DefaultMaxBinaryBytes"/> is 8 MiB. Images are not charged against the context
///     window by the same route as text, so a byte ceiling is the right control for them. That
///     ceiling sits at or below the per-attachment ceilings the major providers publish, so
///     content this library accepts is content a provider will accept.
///     </para>
///     <para>
///     <see cref="DefaultMaxAttachmentsPerTurn"/> is 4. This is a liveness and cost control
///     rather than a safety control: an agent that attaches a dozen images in one turn exhausts
///     the provider's per-request budget and stalls, and the resulting error is not something a
///     model can reason its way out of.
///     </para>
///     <para>
///     <see cref="DefaultMaxAgentDepth"/> is 2. It bounds how deep a chain of delegated agents may
///     run — a root agent at depth zero may start a child, and that child may start one more. It
///     sits here rather than as a constant on the delegating tool because it is the same kind of
///     thing as the ceilings above: a budget the host sets once and every tool observes.
///     Delegation spends the host's money and time in a way the root agent's own context window
///     never reveals, and an agent that can delegate can delegate to something that delegates,
///     so an unbounded chain is a runaway the model cannot see and the host did not sanction.
///     Two levels is enough for the pattern delegation exists to serve — a coordinator, a worker,
///     and a specialist the worker consults — while keeping the worst case a host can be billed
///     for finite and small.
///     </para>
///     <para>
///     <b>These values are provisional.</b> They are public API defaults and they appear in
///     requirement text, so they are to be confirmed by the repository owner before the first
///     tagged release. Nothing is published yet, so they remain freely changeable until then.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     A tool bounds its own output against the policy's result ceiling, refusing rather than
///     returning something that would crowd out the conversation that follows it. The ceilings are
///     carried on the <see cref="PathPolicy"/> a tool is given, so a tool reads
///     <see cref="MaxResultCharacters"/> from there rather than inventing a limit of its own.
///     </para>
///     <code>
///     var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);
///
///     // A structured result a tool has assembled and is about to return.
///     var listing = string.Join("\n", Enumerable.Range(1, 5000).Select(n => "section " + n));
///
///     object result = listing.Length > policy.Limits.MaxResultCharacters
///         ? ToolResult.Denied(DenialReason.ResourceTooLarge, "The listing exceeds the result limit.")
///         : ToolResult.Structured(new { listing });
///     </code>
/// </example>
public sealed class ToolLimits
{
    /// <summary>
    ///     The default ceiling on the bytes a tool may read from one source.
    /// </summary>
    public const int DefaultMaxReadBytes = 64 * 1024;

    /// <summary>
    ///     The default ceiling on the characters a tool result may return to the model.
    /// </summary>
    public const int DefaultMaxResultCharacters = 32_000;

    /// <summary>
    ///     The default ceiling on the bytes of binary content a tool may return.
    /// </summary>
    public const int DefaultMaxBinaryBytes = 8 * 1024 * 1024;

    /// <summary>
    ///     The default ceiling on the attachments a tool may add in one turn.
    /// </summary>
    public const int DefaultMaxAttachmentsPerTurn = 4;

    /// <summary>
    ///     The default ceiling on how deep a chain of delegated agents may run.
    /// </summary>
    /// <remarks>
    ///     The root agent an application starts is at depth zero, so this value counts the
    ///     delegated agents beneath it: at 2, a root agent may start a child and that child may
    ///     start one more, and the grandchild's own attempt to delegate is refused.
    /// </remarks>
    public const int DefaultMaxAgentDepth = 2;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolLimits"/> class.
    /// </summary>
    /// <remarks>
    ///     Every parameter is optional and defaults to the corresponding published constant, so
    ///     a host replaces one ceiling — <c>new ToolLimits(maxBinaryBytes: 1024)</c> — without
    ///     restating the others and without a builder existing solely to make that convenient.
    ///     <b>Zero is permitted</b> on every ceiling: a zero ceiling is the expressible way to
    ///     disable an operation entirely, and is a meaningful host configuration rather than a
    ///     mistake. A negative ceiling has no meaning at all and is rejected.
    /// </remarks>
    /// <param name="maxReadBytes">
    ///     The ceiling on the bytes a tool may read from one source. Must not be negative.
    /// </param>
    /// <param name="maxResultCharacters">
    ///     The ceiling on the characters a tool result may return. Must not be negative.
    /// </param>
    /// <param name="maxBinaryBytes">
    ///     The ceiling on the bytes of binary content a tool may return. Must not be negative.
    /// </param>
    /// <param name="maxAttachmentsPerTurn">
    ///     The ceiling on the attachments a tool may add in one turn. Must not be negative.
    /// </param>
    /// <param name="maxAgentDepth">
    ///     The ceiling on how deep a chain of delegated agents may run, counted from a root agent
    ///     at depth zero. Must not be negative; zero forbids delegation entirely.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when any of the supplied ceilings is negative.
    /// </exception>
    public ToolLimits(
        int maxReadBytes = DefaultMaxReadBytes,
        int maxResultCharacters = DefaultMaxResultCharacters,
        int maxBinaryBytes = DefaultMaxBinaryBytes,
        int maxAttachmentsPerTurn = DefaultMaxAttachmentsPerTurn,
        int maxAgentDepth = DefaultMaxAgentDepth)
    {
        // Validate before any assignment so a rejected instance never exists even briefly. A
        // negative ceiling is a programming error in the host's configuration code, not a
        // runtime condition a model can provoke, so it is surfaced rather than clamped.
        ArgumentOutOfRangeException.ThrowIfNegative(maxReadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResultCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBinaryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAttachmentsPerTurn);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAgentDepth);

        MaxReadBytes = maxReadBytes;
        MaxResultCharacters = maxResultCharacters;
        MaxBinaryBytes = maxBinaryBytes;
        MaxAttachmentsPerTurn = maxAttachmentsPerTurn;
        MaxAgentDepth = maxAgentDepth;
    }

    /// <summary>
    ///     Gets the limits a host receives when it configures nothing.
    /// </summary>
    /// <remarks>
    ///     A single shared instance rather than a factory method, because the type is immutable
    ///     and sharing it is therefore free of risk. Callers may compare against it by reference
    ///     to establish that no host configuration was applied.
    /// </remarks>
    public static ToolLimits Default { get; } = new();

    /// <summary>
    ///     Gets the ceiling on the bytes a tool may read from one source.
    /// </summary>
    public int MaxReadBytes { get; }

    /// <summary>
    ///     Gets the ceiling on the characters a tool result may return to the model.
    /// </summary>
    public int MaxResultCharacters { get; }

    /// <summary>
    ///     Gets the ceiling on the bytes of binary content a tool may return.
    /// </summary>
    public int MaxBinaryBytes { get; }

    /// <summary>
    ///     Gets the ceiling on the attachments a tool may add in one turn.
    /// </summary>
    public int MaxAttachmentsPerTurn { get; }

    /// <summary>
    ///     Gets the ceiling on how deep a chain of delegated agents may run.
    /// </summary>
    /// <remarks>
    ///     Counted from a root agent at depth zero, so the value is the number of delegated
    ///     agents that may stack beneath it. Zero forbids delegation entirely, which is the
    ///     expressible way for a host to attach a delegating family and then withhold its use.
    /// </remarks>
    public int MaxAgentDepth { get; }
}
