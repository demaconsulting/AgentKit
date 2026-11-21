using System.Text.Json;

namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     A local MCP server that proves whether it was executed: its command appends a line to a
///     marker file, then waits.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why a marker rather than the runtime's own account.</b> The runtime's MCP listing reports
///     configuration and enablement, not execution, and its running check reports a server only once
///     it has completed the MCP handshake — which this probe never does. A file the server's own
///     process writes is the one observation that separates "configured" from "started", so the live
///     suite asserts on it. A positive control shows the same probe does write its marker when the
///     protection under test is absent, which keeps a missing marker from passing vacuously.
///     </para>
///     <para>
///     Everything lives under the run's root: the script, the marker and the configuration files
///     written for it. Nothing is written to the user's own Copilot home or workspace. The command is
///     <c>cmd.exe /d /c</c> on Windows and <c>/bin/sh</c> elsewhere.
///     </para>
/// </remarks>
internal sealed class McpMarkerProbe
{
    /// <summary>
    ///     How long a positive control waits for the marker, and a negative scenario waits before
    ///     asserting it is absent.
    /// </summary>
    /// <remarks>
    ///     The probe servers in the planning probes wrote their markers at session creation, within a
    ///     second; the window leaves a wide margin, and each control logs its measured latency.
    /// </remarks>
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Initializes a new instance of the <see cref="McpMarkerProbe"/> class, writing its script.
    /// </summary>
    /// <param name="root">The folder under the run's root that holds the probe's script and marker.</param>
    /// <param name="name">The server's name, unique within the run.</param>
    internal McpMarkerProbe(string root, string name)
    {
        Name = name;
        MarkerPath = Path.Combine(root, "markers", $"{name}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        Directory.CreateDirectory(Path.Combine(root, "scripts"));

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(root, "scripts", $"{name}.cmd");
            File.WriteAllText(script, $"@echo off\r\necho started>> \"{MarkerPath}\"\r\nping -n 31 127.0.0.1 > nul\r\n");
            Command = "cmd.exe";
            Args = ["/d", "/c", script];
        }
        else
        {
            var script = Path.Combine(root, "scripts", $"{name}.sh");
            File.WriteAllText(script, $"#!/bin/sh\necho started >> '{MarkerPath}'\nsleep 30\n");
            Command = "/bin/sh";
            Args = [script];
        }
    }

    /// <summary>
    ///     Gets the server's name.
    /// </summary>
    internal string Name { get; }

    /// <summary>
    ///     Gets the path of the file the server writes when it is executed.
    /// </summary>
    internal string MarkerPath { get; }

    /// <summary>
    ///     Gets a value indicating whether the server has been executed.
    /// </summary>
    internal bool Executed => File.Exists(MarkerPath);

    /// <summary>
    ///     Gets the command that runs the server's script.
    /// </summary>
    private string Command { get; }

    /// <summary>
    ///     Gets the arguments that run the server's script.
    /// </summary>
    private string[] Args { get; }

    /// <summary>
    ///     Composes an MCP configuration file — the form of a data directory's <c>mcp-config.json</c>
    ///     and a workspace's <c>.mcp.json</c> — that configures this server.
    /// </summary>
    /// <returns>The configuration's JSON.</returns>
    internal string McpConfigJson() =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                [Name] = new Dictionary<string, object>
                {
                    ["type"] = "local",
                    ["command"] = Command,
                    ["args"] = Args,
                    ["tools"] = new[] { "*" },
                },
            },
        });

    /// <summary>
    ///     Waits for the server to write its marker.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>How long the marker took to appear, or <see langword="null"/> when it did not within <see cref="Window"/>.</returns>
    internal async Task<TimeSpan?> WaitForMarkerAsync(CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < Window)
        {
            if (Executed)
            {
                return DateTime.UtcNow - started;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return Executed ? DateTime.UtcNow - started : null;
    }
}
