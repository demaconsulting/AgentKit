using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Prints and records the tool calls a turn made, so a reader can watch the agent plan,
///     remember and delegate rather than take the claim on faith.
/// </summary>
/// <remarks>
///     <para>
///     Shared by both conversation shapes. The Copilot conversation observes tool activity in the
///     agent's streamed updates; the compacting conversation observes it in the chat client beneath
///     it, because a session turn hands back an answer and nothing else. Extracting the rendering
///     here is what keeps the two from drifting into two different transcripts of the same run.
///     </para>
///     <para>
///     This class is static, holds no state beyond its serializer settings, and writes only to the
///     console and to a caller-supplied writer.
///     </para>
/// </remarks>
public static class ToolTrace
{
    /// <summary>
    ///     The serializer settings used to print a memory result whole, one field per line.
    /// </summary>
    /// <remarks>
    ///     Indentation is the whole point: an un-truncated <c>memory_recall</c> is roughly fourteen
    ///     hundred characters, which is unreadable as one wrapped line and entirely readable as a
    ///     field per line. Nothing is dropped, because what makes the demonstration checkable is
    ///     that every returned match is visible.
    /// </remarks>
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>
    ///     Prints a tool call with its correlating identifier, name and arguments, so a user sees
    ///     exactly what the agent asked the tool to do and can match the result to it.
    /// </summary>
    /// <param name="call">The function call to print. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> is <see langword="null"/>.</exception>
    public static void PrintCall(FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);

        var arguments = call.Arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(call.Arguments)
            : "{}";

        Console.WriteLine($"\n  [tool call {call.CallId}] {call.Name} {arguments}");
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
    /// <param name="call">The function call to record. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> is <see langword="null"/>.</exception>
    public static void Record(TextWriter? transcript, FunctionCallContent call)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (transcript is null)
        {
            return;
        }

        transcript.WriteLine(call.Name);
        transcript.Flush();
    }

    /// <summary>
    ///     Prints a single-line summary of a tool result, naming the call it answers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A refusal's text is exactly what proves the mechanism held — a near-duplicate declined,
    ///     a write into the read-only corpus denied — so it is shown rather than elided.
    ///     </para>
    ///     <para>
    ///     <b>Memory results are printed whole and indented; everything else is truncated to one
    ///     line.</b> A truncated <c>memory_recall</c> was observed showing one of five returned
    ///     matches, which makes the one demonstration this sample exists for impossible to check by
    ///     reading. Printing it whole then produced the opposite problem — a single line of some
    ///     fourteen hundred characters that wraps into an unreadable block — so the full content is
    ///     kept and the shape is given back to it instead: one field per line, indented under the
    ///     header. Re-truncating would trade a readability problem for the inability to check the
    ///     demonstration at all, and only the second is fatal. A recall is bounded by the author's
    ///     configured count and a file read is not, so the one-line limit stays where unbounded
    ///     output actually comes from.
    ///     </para>
    /// </remarks>
    /// <param name="result">The function result to summarize. Must not be <see langword="null"/>.</param>
    /// <param name="call">The call it answers, or <see langword="null"/> if it was not seen.</param>
    /// <param name="memoryFamilyPrefix">
    ///     The tool-name prefix whose results are printed whole. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="result"/> or <paramref name="memoryFamilyPrefix"/> is <see langword="null"/>.
    /// </exception>
    public static void PrintResult(
        FunctionResultContent result,
        FunctionCallContent? call,
        string memoryFamilyPrefix)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(memoryFamilyPrefix);

        var name = call?.Name ?? "(unmatched call)";
        var memoryResult = call?.Name?.StartsWith(memoryFamilyPrefix + "_", StringComparison.Ordinal) == true;

        if (!memoryResult)
        {
            Console.WriteLine($"  [tool result {result.CallId}] {name} -> {Summarize(result.Result)}");
            return;
        }

        Console.WriteLine($"  [tool result {result.CallId}] {name} ->");
        foreach (var line in Expand(result.Result))
        {
            Console.WriteLine($"      {line}");
        }
    }

    /// <summary>
    ///     Renders a tool result value as indented lines, keeping every character of it.
    /// </summary>
    /// <param name="value">The result value, which may be <see langword="null"/>.</param>
    /// <returns>The lines to print beneath the header, with no line left blank.</returns>
    private static IEnumerable<string> Expand(object? value)
    {
        var text = value switch
        {
            null => "(no result)",
            string stringValue => stringValue,
            _ => JsonSerializer.Serialize(value, IndentedJson),
        };

        return text
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.TrimEnd());
    }

    /// <summary>
    ///     Renders a tool result value as a single-line string for the console.
    /// </summary>
    /// <param name="value">The result value, which may be <see langword="null"/>.</param>
    /// <returns>A single-line description of the value.</returns>
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
