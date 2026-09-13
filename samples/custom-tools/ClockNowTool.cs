using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     A custom, author-written guarded tool that reports the current date and time, both local and
///     in UTC.
/// </summary>
/// <remarks>
///     <para>
///     This tool is the sample's <b>deliberate contrast</b> to <see cref="DocStatsWordCountTool"/>.
///     It takes no path — indeed no arguments at all — and it consults no <see cref="PathPolicy"/>,
///     because it touches nothing a policy governs. Its purpose is to show that
///     <see cref="GuardedToolFactory"/> is the construction path for <em>every</em> tool, not only
///     path-based ones: the guarded factory is how a tool is built regardless of whether it reads
///     files, and a tool that needs no policy simply does not accept or consult one.
///     </para>
///     <para>
///     The result is returned through <see cref="ToolResult.Structured"/> so a model receives a
///     machine-readable shape — the local time, the UTC time, and the time-zone identifier — rather
///     than a sentence it would have to parse back into fields.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class ClockNowTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant so the pack that claims the family prefix and any test can name
    ///     the tool without repeating a string literal. It carries the <c>clock</c> family prefix,
    ///     which <see cref="ToolPackBuilder.Build"/> verifies against <see cref="ClockToolPack"/>.
    /// </remarks>
    public const string ToolName = "clock_now";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reports the current date and time, both local and in UTC, together with the local "
        + "time-zone identifier. Takes no arguments. Returns a structured result.";

    /// <summary>
    ///     Creates the <c>clock_now</c> tool.
    /// </summary>
    /// <remarks>
    ///     Unlike a path-taking tool's factory, this one accepts no <see cref="PathPolicy"/>: the
    ///     tool governs nothing a policy could constrain, so consulting one would be theatre. It is
    ///     still built through <see cref="GuardedToolFactory"/>, because that is the single supported
    ///     construction path — the guarantee it enforces (a validated name, a mandatory description,
    ///     and correct result delivery) is exactly as relevant to a no-path tool as to any other.
    /// </remarks>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    public static AIFunction Create()
    {
        // A parameterless delegate: the tool needs nothing from the model. It is declared to return
        // object because it returns a structured result, the shape GuardedToolFactory serializes.
        var now = () => Now();

        return GuardedToolFactory.Create(now, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Builds the current-time result.
    /// </summary>
    /// <remarks>
    ///     Both instants are captured from a single <see cref="DateTimeOffset.Now"/> reading so the
    ///     local and UTC values describe the same moment rather than two moments a few ticks apart.
    ///     Round-trip ("O") formatting is used so a model receives an unambiguous, parseable string.
    /// </remarks>
    /// <returns>A structured result naming the local time, the UTC time, and the local time zone.</returns>
    private static object Now()
    {
        var now = DateTimeOffset.Now;

        return ToolResult.Structured(new
        {
            localTime = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            utcTime = now.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            timeZone = TimeZoneInfo.Local.Id,
        });
    }
}
