using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Drives the conversation with a built agent: an interactive turn loop, or a sequence of
///     stated prompts, sharing one session so the plan, the memories and the conversation carry
///     across turns.
/// </summary>
/// <remarks>
///     <para>
///     This loop is entirely provider-neutral — it is handed an <see cref="AIAgent"/> and never
///     learns which provider produced it, nor which embedding backend stands behind its memories.
///     </para>
///     <para>
///     Printing every tool call and result is the point of the sample rather than decoration. This
///     is what a reader watches to see a plan being written down before work starts, a finding
///     being filed with its source, a near-duplicate being <em>refused</em> with the conflicting
///     memory named, a correction going through <c>memory_revise</c> with a new citation, and a
///     child agent being started with a task the parent stated.
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
    ///     A single <see cref="AgentSession"/> is created up front and reused for every turn. That
    ///     matters more here than in a single-subject assistant: the task list and the memories are
    ///     the agent's own state across turns, and a fresh session per prompt would hide exactly the
    ///     behavior the sample exists to show.
    /// </remarks>
    /// <param name="agent">The agent to converse with. Must not be <see langword="null"/>.</param>
    /// <param name="options">The options selecting stated prompts or interactive mode.</param>
    /// <param name="transcript">
    ///     A writer receiving one machine-readable line per tool call, or <see langword="null"/>
    ///     for none.
    /// </param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to end the run cleanly.</param>
    /// <returns>A task that completes when the conversation ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> or <paramref name="options"/> is null.</exception>
    public static async Task RunAsync(
        AIAgent agent,
        CommandLineOptions options,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(options);

        var session = await agent.CreateSessionAsync(cancellationToken);

        if (options.Prompts.Count > 0)
        {
            foreach (var prompt in options.Prompts)
            {
                await RunTurnAsync(agent, session, prompt, transcript, cancellationToken);
            }

            return;
        }

        await RunInteractiveAsync(agent, session, transcript, cancellationToken);
    }

    /// <summary>
    ///     Runs the interactive read-eval-print loop until the user exits, reaches end-of-input, or
    ///     cancels.
    /// </summary>
    /// <param name="agent">The agent to converse with.</param>
    /// <param name="session">The shared session carrying conversation state.</param>
    /// <param name="transcript">The tool-call transcript writer, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    private static async Task RunInteractiveAsync(
        AIAgent agent,
        AgentSession session,
        TextWriter? transcript,
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
                await RunTurnAsync(agent, session, input, transcript, cancellationToken);
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
    /// <param name="agent">The agent running the turn.</param>
    /// <param name="session">The shared session, so this turn sees prior turns.</param>
    /// <param name="message">The user's message for this turn.</param>
    /// <param name="transcript">The tool-call transcript writer, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancelled on Ctrl-C to abort the turn.</param>
    /// <returns>A task that completes when the turn's stream is exhausted.</returns>
    private static async Task RunTurnAsync(
        AIAgent agent,
        AgentSession session,
        string message,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"\nyou> {message}");
        Console.Write("\nassistant> ");

        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                Console.Write(update.Text);
            }

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        PrintToolCall(call);
                        RecordToolCall(transcript, call);
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
    /// <param name="call">The function call to print.</param>
    private static void PrintToolCall(FunctionCallContent call)
    {
        var arguments = call.Arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(call.Arguments)
            : "{}";

        Console.WriteLine($"\n  [tool call] {call.Name} {arguments}");
    }

    /// <summary>
    ///     Appends one machine-readable line naming a tool that was called.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Only the tool's name is written, never its arguments. The transcript exists so an
    ///     unattended run can be checked — did this agent actually plan, file, recall and delegate,
    ///     or did it merely talk about doing so? — and a tool name answers that while a serialized
    ///     argument could carry the contents of a document into a log.
    ///     </para>
    ///     <para>
    ///     The writer is flushed per line so that a run killed by a CI timeout still leaves behind
    ///     everything it had done up to that point, which is precisely the run whose transcript is
    ///     most worth reading.
    ///     </para>
    /// </remarks>
    /// <param name="transcript">The writer, or <see langword="null"/> when no transcript was asked for.</param>
    /// <param name="call">The function call to record.</param>
    private static void RecordToolCall(TextWriter? transcript, FunctionCallContent call)
    {
        if (transcript is null)
        {
            return;
        }

        transcript.WriteLine(call.Name);
        transcript.Flush();
    }

    /// <summary>
    ///     Prints a brief, single-line summary of a tool result, including a refusal's guidance.
    /// </summary>
    /// <remarks>
    ///     A refusal's text is exactly what proves the mechanism held — a near-duplicate declined,
    ///     a write into the read-only corpus denied — so it is shown, trimmed to one readable line.
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
    /// <param name="value">The result value, which may be <see langword="null"/>.</param>
    /// <returns>A short single-line description of the value.</returns>
    private static string Summarize(object? value)
    {
        const int maxLength = 300;

        var text = value switch
        {
            null => "(no result)",
            string stringValue => stringValue,
            _ => JsonSerializer.Serialize(value),
        };

        // Flatten newlines and runs of whitespace so a multi-line body becomes one line.
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
