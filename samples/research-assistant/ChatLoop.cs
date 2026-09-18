using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Drives the conversation with a built agent: an interactive turn loop, or a sequence of
///     stated prompts, sharing one conversation so the plan, the memories and the context carry
///     across turns.
/// </summary>
/// <remarks>
///     <para>
///     This loop is entirely provider-neutral — it is handed a <see cref="Conversation"/> and never
///     learns which provider produced it, which session shape carries it, nor which embedding
///     backend stands behind its memories.
///     </para>
///     <para>
///     Printing every tool call and result is the point of the sample rather than decoration. This
///     is what a reader watches to see a plan being written down before work starts, a finding
///     being filed with its source, a near-duplicate being <em>refused</em> with the conflicting
///     memory named, a correction going through <c>memory_revise</c> with a new citation, and a
///     child agent being started with a task the parent stated.
///     </para>
///     <para>
///     <b>What a compacting session reports is printed alongside it.</b> A turn that rotated, a
///     compaction level that has climbed, and history that had to be dropped outright are the three
///     facts that distinguish a long-running agent that is coping from one that is quietly losing
///     its record. AgentKit reports all three on every turn; an application that never looks is
///     unaffected, and this one looks.
///     </para>
/// </remarks>
public static class ChatLoop
{
    /// <summary>
    ///     The words that end an interactive session, matched case-insensitively.
    /// </summary>
    private static readonly string[] ExitWords = ["exit", "quit"];

    /// <summary>
    ///     Runs the conversation according to the options: the stated prompts in order, or an
    ///     interactive loop.
    /// </summary>
    /// <remarks>
    ///     A single conversation is started up front and reused for every turn. That matters more
    ///     here than in a single-subject assistant: the task list and the memories are the agent's
    ///     own state across turns, and a fresh conversation per prompt would hide exactly the
    ///     behavior the sample exists to show — including the compaction, which cannot happen at
    ///     all until one conversation has run long enough to fill a window.
    /// </remarks>
    /// <param name="setup">The built agent and its conversation plans. Must not be <see langword="null"/>.</param>
    /// <param name="options">The options selecting stated prompts or interactive mode.</param>
    /// <param name="transcript">
    ///     A writer receiving one machine-readable line per tool call, or <see langword="null"/>
    ///     for none.
    /// </param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to end the run cleanly.</param>
    /// <returns>A task that completes when the conversation ends.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="setup"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public static async Task RunAsync(
        AgentSetup setup,
        CommandLineOptions options,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(options);

        await using var conversation = await Conversation.StartAsync(
            setup.Conversation,
            transcript,
            cancellationToken);

        if (options.Prompts.Count > 0)
        {
            foreach (var prompt in options.Prompts)
            {
                await RunTurnAsync(conversation, prompt, cancellationToken);
            }

            ReportSessionTotals(conversation);
            return;
        }

        await RunInteractiveAsync(conversation, cancellationToken);
    }

    /// <summary>
    ///     Runs one question on the recall agent, in a conversation of its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The fresh conversation is half of what this turn proves, and the caller cannot supply
    ///     it.</b> The recall agent carries the memory family and no reading tool, so no document
    ///     is reachable from it; starting a new conversation here means no earlier turn's document
    ///     contents are in its context either. With both gone, an answer that states a fact from
    ///     the corpus can only have arrived through <c>memory_recall</c> — which is what the
    ///     ordinary prompts, sharing one conversation with the turns that read the documents, could
    ///     never establish.
    ///     </para>
    ///     <para>
    ///     <b>The memories themselves are untouched by any of this.</b> A compacting session
    ///     consolidates the <em>conversation</em>; the memory store is the application's, lives
    ///     outside the session entirely, and survives every rotation intact. That separation is why
    ///     an agent whose window has turned over several times can still answer from what it filed
    ///     on its first turn.
    ///     </para>
    /// </remarks>
    /// <param name="plan">The memory-only conversation plan. Must not be <see langword="null"/>.</param>
    /// <param name="question">The question to answer from memory alone.</param>
    /// <param name="transcript">The tool-call transcript writer, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to end the run cleanly.</param>
    /// <returns>A task that completes when the recall turn ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    public static async Task RunRecallAsync(
        ConversationPlan plan,
        string question,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Console.WriteLine(
            "\n--- recall turn: a fresh conversation on an agent with the memory tools and no way "
            + "to read anything ---");

        await using var conversation = await Conversation.StartAsync(plan, transcript, cancellationToken);
        await RunTurnAsync(conversation, question, cancellationToken);
    }

    /// <summary>
    ///     Runs the interactive read-eval-print loop until the user exits, reaches end-of-input, or
    ///     cancels.
    /// </summary>
    /// <param name="conversation">The shared conversation carrying state across turns.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private static async Task RunInteractiveAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Research assistant ready. Type a message, or 'exit' to quit.");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("\nyou> ");

            // ReadLine returns null at end-of-input; that is a clean way to leave, the same as
            // typing 'exit'.
            var input = Console.ReadLine();
            if (input is null)
            {
                Console.WriteLine();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (Array.Exists(ExitWords, word => string.Equals(word, input.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            try
            {
                await RunTurnAsync(conversation, input, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ctrl-C during a turn: stop the turn and fall out of the loop cleanly.
                Console.WriteLine();
                break;
            }
        }

        ReportSessionTotals(conversation);
        Console.WriteLine("Goodbye.");
    }

    /// <summary>
    ///     Runs one conversation turn and reports what the session made of it.
    /// </summary>
    /// <param name="conversation">The shared conversation, so this turn sees prior turns.</param>
    /// <param name="message">The user's message for this turn.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to abort the turn.</param>
    /// <returns>A task that completes when the turn ends.</returns>
    private static async Task RunTurnAsync(
        Conversation conversation,
        string message,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\nyou> {message}");
        Console.Write("\nassistant> ");

        var compaction = await conversation.AskAsync(message, cancellationToken);

        Console.WriteLine();
        ReportCompaction(conversation, compaction);
    }

    /// <summary>
    ///     Prints what a compacting session reported about the turn that has just run.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Three facts, each acted on differently, which is why AgentKit reports them separately.
    ///     <b>Occupancy</b> is printed every turn, because watching it climb toward the window is
    ///     what makes the next rotation comprehensible rather than sudden. <b>A rotation</b> is
    ///     printed when it happens, with the running cost in summarizer calls beside it, because
    ///     consolidation is the expensive part of this arrangement and an application that cannot
    ///     see the cost cannot reason about it. <b>Dropped material</b> is printed as a warning,
    ///     because it is the one signal that compaction bought nothing and the agent's record of
    ///     its own work is now incomplete.
    ///     </para>
    ///     <para>
    ///     Nothing here is written to the machine-readable transcript. That file is a record of tool
    ///     names, and a line of another kind in it would change what an unattended check is reading.
    ///     </para>
    /// </remarks>
    /// <param name="conversation">The conversation, for the running totals a turn does not carry.</param>
    /// <param name="compaction">
    ///     What the turn reported, or <see langword="null"/> on a provider-managed conversation.
    /// </param>
    private static void ReportCompaction(Conversation conversation, AgentSessionResponse? compaction)
    {
        if (compaction is null || conversation.Session is not { } session)
        {
            return;
        }

        var usage = compaction.Usage;

        // A turn that rotated hands back the replacement session's usage, and a replacement has not
        // spoken yet - so on a provider that answers for its own window there is nothing to report
        // until the next turn. Printing the placeholder as though it were a reading ("0 of 1 tokens")
        // reads as a defect; saying the figure is not in yet is what is actually true.
        Console.WriteLine(
            usage.WindowTokens <= 1 && usage.ConversationTokens == 0
                ? $"\n  [session] occupancy not yet reported by the provider, compaction level {compaction.Level}"
                : $"\n  [session] conversation {usage.ConversationTokens} of {usage.WindowTokens} tokens"
                  + $" (overhead {usage.OverheadTokens}), compaction level {compaction.Level}");

        if (compaction.RotationOccurred)
        {
            Console.WriteLine(
                "  [session] rotated — older history consolidated into a fresh provider session."
                + $" Rotations: {session.RotationCount}. Summarizer calls so far:"
                + $" {session.ConsolidationCount}.");
        }

        if (compaction.MaterialDropped)
        {
            Console.WriteLine(
                "  [session] WARNING: history was dropped outright. Compacting at level "
                + $"{compaction.Level} did not free enough room, so the oldest record was "
                + "discarded. The agent's own account of that work is now gone; its filed memories "
                + "are not, because they live outside the session.");
        }
    }

    /// <summary>
    ///     Prints what the whole conversation cost, once it has ended.
    /// </summary>
    /// <remarks>
    ///     A conversation that never rotated says so, which is worth printing: it is the difference
    ///     between "compaction worked" and "compaction was never reached", and the two look
    ///     identical from a transcript of answers alone.
    /// </remarks>
    /// <param name="conversation">The conversation that has just ended.</param>
    private static void ReportSessionTotals(Conversation conversation)
    {
        if (conversation.Session is not { } session)
        {
            return;
        }

        Console.WriteLine(
            $"\n[session] ended after {session.RotationCount} rotation(s) and "
            + $"{session.ConsolidationCount} summarizer call(s), at compaction level "
            + $"{session.Level}.");
    }
}
