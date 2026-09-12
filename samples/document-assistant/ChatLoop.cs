using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     Drives the conversation with a built agent: a single interactive turn loop, or one
///     non-interactive prompt, sharing one session so context is retained across turns.
/// </summary>
/// <remarks>
///     <para>
///     This loop is entirely provider-neutral — it is handed an <see cref="AIAgent"/> and never
///     learns which provider produced it. That is the payoff of confining all provider knowledge to
///     the composition: the interesting behavior (streaming text, and above all the visible tool
///     calls) is written once and runs unchanged everywhere.
///     </para>
///     <para>
///     Printing each tool call and its result is the point of the sample, not decoration: it makes
///     the safety mechanism observable. A user watches the agent ask for <c>text_file_read</c> with
///     a path, and watches a refusal come back naming the correct approach, rather than having to
///     take the library's guarantees on faith.
///     </para>
/// </remarks>
public static class ChatLoop
{
    /// <summary>
    ///     The words that end an interactive session, matched case-insensitively.
    /// </summary>
    private static readonly string[] ExitWords = ["exit", "quit"];

    /// <summary>
    ///     Runs the conversation according to the options: one prompt, or an interactive REPL.
    /// </summary>
    /// <remarks>
    ///     A single <see cref="AgentSession"/> is created up front and reused for every turn, which
    ///     is what preserves conversation state (the agent remembers earlier turns). Single-prompt
    ///     mode runs exactly one turn on that session and returns, so the two modes share the same
    ///     turn machinery.
    /// </remarks>
    /// <param name="agent">The agent to converse with. Must not be <see langword="null"/>.</param>
    /// <param name="options">The options selecting single-prompt or interactive mode.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to end the run cleanly.</param>
    /// <returns>A task that completes when the conversation ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> or <paramref name="options"/> is null.</exception>
    public static async Task RunAsync(AIAgent agent, CommandLineOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(options);

        // Create the session once and reuse it: this is what carries context across turns in the
        // interactive loop, and it costs nothing in single-prompt mode.
        var session = await agent.CreateSessionAsync(cancellationToken);

        if (options.Prompt is not null)
        {
            await RunTurnAsync(agent, session, options.Prompt, cancellationToken);
            return;
        }

        await RunInteractiveAsync(agent, session, cancellationToken);
    }

    /// <summary>
    ///     Runs the interactive read-eval-print loop until the user exits, reaches end-of-input, or
    ///     cancels.
    /// </summary>
    /// <remarks>
    ///     The loop ends on an explicit <c>exit</c>/<c>quit</c>, on end-of-input (a null line, e.g.
    ///     piped input running out or Ctrl-Z/Ctrl-D), or on cancellation (Ctrl-C) — three ways to
    ///     leave, all clean. Blank lines are ignored rather than sent as empty turns.
    /// </remarks>
    /// <param name="agent">The agent to converse with.</param>
    /// <param name="session">The shared session carrying conversation state.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private static async Task RunInteractiveAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Document assistant ready. Type a message, or 'exit' to quit.");

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
                await RunTurnAsync(agent, session, input, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ctrl-C during a turn: stop the turn and fall out of the loop cleanly.
                Console.WriteLine();
                break;
            }
        }

        Console.WriteLine("Goodbye.");
    }

    /// <summary>
    ///     Runs one conversation turn, streaming the assistant's text and printing every tool call
    ///     and result as it happens.
    /// </summary>
    /// <remarks>
    ///     The streamed updates carry two interleaved things: assistant text (written as it
    ///     arrives) and tool activity (function calls and their results). Both are surfaced — the
    ///     text because it is the answer, the tool activity because its visibility is the sample's
    ///     entire reason for existing.
    /// </remarks>
    /// <param name="agent">The agent running the turn.</param>
    /// <param name="session">The shared session, so this turn sees prior turns.</param>
    /// <param name="message">The user's message for this turn.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to abort the turn.</param>
    /// <returns>A task that completes when the turn's stream is exhausted.</returns>
    private static async Task RunTurnAsync(
        AIAgent agent,
        AgentSession session,
        string message,
        CancellationToken cancellationToken)
    {
        Console.Write("\nassistant> ");

        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken))
        {
            // Assistant text is streamed straight through so a long answer appears as it is
            // produced rather than all at once at the end.
            if (!string.IsNullOrEmpty(update.Text))
            {
                Console.Write(update.Text);
            }

            // Tool activity is interleaved with text; surface each item on its own line so the
            // safety mechanism is legible against the streamed prose.
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        PrintToolCall(call);
                        break;

                    case FunctionResultContent result:
                        PrintToolResult(result);
                        break;
                }
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    ///     Prints a tool call with its name and arguments, so a user sees exactly what the agent
    ///     asked the tool to do.
    /// </summary>
    /// <remarks>
    ///     Arguments are rendered as compact JSON; showing the requested path is what lets a user
    ///     connect a request to the containment decision that follows it.
    /// </remarks>
    /// <param name="call">The function call to print.</param>
    private static void PrintToolCall(FunctionCallContent call)
    {
        var arguments = call.Arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(call.Arguments)
            : "{}";

        Console.WriteLine($"\n  [tool call] {call.Name} {arguments}");
    }

    /// <summary>
    ///     Prints a brief, single-line summary of a tool result, including a refusal's guidance.
    /// </summary>
    /// <remarks>
    ///     The result is summarized rather than dumped: a full file body would drown the transcript,
    ///     but a refusal's text is exactly what proves the containment boundary held, so it is shown
    ///     in full (trimmed to one readable line).
    /// </remarks>
    /// <param name="result">The function result to summarize.</param>
    private static void PrintToolResult(FunctionResultContent result)
    {
        var summary = Summarize(result.Result);
        Console.WriteLine($"  [tool result] {result.CallId} -> {summary}");
    }

    /// <summary>
    ///     Renders a tool result value as a short, single-line string for the transcript.
    /// </summary>
    /// <remarks>
    ///     A tool returns text, structured data, or content; this collapses any of them to a bounded
    ///     one-liner (whitespace flattened, length capped) so the transcript stays readable while
    ///     still revealing whether the call succeeded or was refused.
    /// </remarks>
    /// <param name="value">The result value, which may be <see langword="null"/>.</param>
    /// <returns>A short single-line description of the value.</returns>
    private static string Summarize(object? value)
    {
        const int maxLength = 200;

        var text = value switch
        {
            null => "(no result)",
            string stringValue => stringValue,
            _ => JsonSerializer.Serialize(value),
        };

        // Flatten newlines and runs of whitespace so a multi-line file body becomes one line.
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
