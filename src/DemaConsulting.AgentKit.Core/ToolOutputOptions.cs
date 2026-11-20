namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Controls how much of a retained tool output a model is shown, and how much a composition
///     may retain, when retention of large tool outputs is composed.
/// </summary>
/// <remarks>
///     <para>
///     <b>These options only matter once an application opts in.</b> Retention is composed with
///     <see cref="ToolPackBuilder.WithToolOutputSpill"/> or <see cref="ToolOutputSpill.Apply"/>; a
///     composition that does neither never consults them. The threshold above which an output is
///     retained is not here: it is <see cref="ToolLimits.MaxResultCharacters"/>, the same ceiling
///     that has always bounded a tool result, so one budget keeps one meaning.
///     </para>
///     <para>
///     <see cref="DefaultPreviewCharacters"/> is 4,000. At roughly four characters to the token that
///     is on the order of a thousand tokens — enough head and tail of a listing, a search result or
///     a report for a model to see its shape and decide where to read, and an eighth of the default
///     result ceiling, so a preview never crowds out the conversation the way the whole output
///     would have.
///     </para>
///     <para>
///     <see cref="DefaultMaxOutputCharacters"/> is 4,194,304 (4 Mi characters). It bounds one
///     retained output. An output larger than that is not a result a model can usefully page
///     through — at the default page size it is well over a hundred pages — and is refused with
///     the ceiling named rather than retained.
///     </para>
///     <para>
///     <see cref="DefaultMaxRetainedCharacters"/> is 16,777,216 (16 Mi characters), which is about
///     32 MiB of UTF-16 text held in memory by one composition. It is four maximum-size outputs,
///     and comfortably more of the outputs a working session actually produces, which are usually
///     tens or hundreds of kilobytes.
///     </para>
///     <para>
///     <see cref="DefaultMaxRetainedOutputs"/> is 256. It bounds the bookkeeping independently of
///     size, so a long session producing many modest outputs is bounded too. When either store
///     ceiling would be exceeded, the oldest outputs are evicted first; a reference to an evicted
///     output is then answered with the fact that it is no longer retained.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Composing a tool list that retains oversized outputs with a smaller preview than the default.
///     </para>
///     <code>
///     var policy = new PathPolicy("/workspace", [PathRule.ReadOnly("/workspace")]);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .WithToolOutputSpill(new ToolOutputOptions(previewCharacters: 2_000))
///         .Build();
///     </code>
/// </example>
public sealed class ToolOutputOptions
{
    /// <summary>
    ///     The default number of characters of a retained output shown to the model as a preview.
    /// </summary>
    public const int DefaultPreviewCharacters = 4_000;

    /// <summary>
    ///     The default ceiling on the characters of one retained output.
    /// </summary>
    public const int DefaultMaxOutputCharacters = 4 * 1024 * 1024;

    /// <summary>
    ///     The default ceiling on the characters one composition retains across every output.
    /// </summary>
    public const int DefaultMaxRetainedCharacters = 16 * 1024 * 1024;

    /// <summary>
    ///     The default ceiling on how many outputs one composition retains.
    /// </summary>
    public const int DefaultMaxRetainedOutputs = 256;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolOutputOptions"/> class.
    /// </summary>
    /// <remarks>
    ///     Every parameter is optional and defaults to the corresponding published constant, so a
    ///     host changes one value without restating the others. A zero preview is permitted: the
    ///     model is then shown only the reference and the line count, which is a legitimate choice
    ///     for a host that wants every large output read deliberately. The retention ceilings must
    ///     be positive, and the store must be able to hold at least one maximum-size output,
    ///     because a ceiling no output could ever satisfy is a configuration mistake rather than a
    ///     choice.
    /// </remarks>
    /// <param name="previewCharacters">
    ///     The characters of a retained output shown to the model, head and tail together. Must not
    ///     be negative.
    /// </param>
    /// <param name="maxOutputCharacters">
    ///     The ceiling on the characters of one retained output. Must be positive.
    /// </param>
    /// <param name="maxRetainedCharacters">
    ///     The ceiling on the characters retained across every output. Must be at least
    ///     <paramref name="maxOutputCharacters"/>.
    /// </param>
    /// <param name="maxRetainedOutputs">
    ///     The ceiling on how many outputs are retained. Must be positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="previewCharacters"/> is negative, when
    ///     <paramref name="maxOutputCharacters"/> or <paramref name="maxRetainedOutputs"/> is not
    ///     positive, or when <paramref name="maxRetainedCharacters"/> is less than
    ///     <paramref name="maxOutputCharacters"/>.
    /// </exception>
    public ToolOutputOptions(
        int previewCharacters = DefaultPreviewCharacters,
        int maxOutputCharacters = DefaultMaxOutputCharacters,
        int maxRetainedCharacters = DefaultMaxRetainedCharacters,
        int maxRetainedOutputs = DefaultMaxRetainedOutputs)
    {
        // Validate before any assignment so a rejected instance never exists even briefly. Each is
        // a mistake in the host's configuration code, not a condition a model can provoke.
        ArgumentOutOfRangeException.ThrowIfNegative(previewCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetainedOutputs);

        // A store too small for one maximum-size output would refuse outputs the per-output ceiling
        // admits, so the two ceilings would contradict each other.
        if (maxRetainedCharacters < maxOutputCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetainedCharacters),
                maxRetainedCharacters,
                "The retained-character ceiling must be at least the per-output ceiling.");
        }

        PreviewCharacters = previewCharacters;
        MaxOutputCharacters = maxOutputCharacters;
        MaxRetainedCharacters = maxRetainedCharacters;
        MaxRetainedOutputs = maxRetainedOutputs;
    }

    /// <summary>
    ///     Gets the options a host receives when it configures nothing.
    /// </summary>
    /// <remarks>
    ///     A single shared instance, because the type is immutable and sharing it is free of risk.
    /// </remarks>
    public static ToolOutputOptions Default { get; } = new();

    /// <summary>
    ///     Gets the characters of a retained output shown to the model, head and tail together.
    /// </summary>
    /// <remarks>
    ///     The preview is further bounded so that it, together with its header, fits within
    ///     <see cref="ToolLimits.MaxResultCharacters"/>.
    /// </remarks>
    public int PreviewCharacters { get; }

    /// <summary>
    ///     Gets the ceiling on the characters of one retained output.
    /// </summary>
    /// <remarks>
    ///     Measured on the output as it is retained — line endings normalized, structured data
    ///     indented — so the ceiling bounds what is held in memory. An output above it is refused
    ///     rather than retained or truncated.
    /// </remarks>
    public int MaxOutputCharacters { get; }

    /// <summary>
    ///     Gets the ceiling on the characters one composition retains across every output.
    /// </summary>
    public int MaxRetainedCharacters { get; }

    /// <summary>
    ///     Gets the ceiling on how many outputs one composition retains.
    /// </summary>
    public int MaxRetainedOutputs { get; }
}
