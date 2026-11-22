namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Where the Copilot runtime AgentKit starts comes from: the Copilot CLI installed on the machine,
///     or the runtime <c>Microsoft.Copilot.SDK</c> provisions into the application's output.
/// </summary>
/// <remarks>
///     <para>
///     The source decides only which executable is spawned. The confinement is the same for both:
///     <c>CopilotClientMode.Empty</c>, a child process over standard input and output, the host's
///     Copilot configuration variables withheld, and a data directory AgentKit owns as the runtime's
///     <c>COPILOT_HOME</c>.
///     </para>
///     <para>
///     <see cref="InstalledCli"/> is the default. The application ships no runtime (its output stays
///     small), and the machine needs a Copilot CLI of at least <see cref="CopilotCli.MinimumVersion"/>;
///     <see cref="CopilotRuntime.StartAsync"/> finds it, checks its version and refuses one that is too
///     old. <see cref="Bundled"/> is the opt-in for machines without a suitable CLI: the host
///     references <c>Microsoft.Copilot.SDK</c> directly, which provisions a runtime of the version the
///     SDK was built for into every output folder (several hundred megabytes per runtime identifier).
///     </para>
/// </remarks>
public enum CopilotRuntimeSource
{
    /// <summary>
    ///     The Copilot CLI installed on the machine: the path the host passes, or the one
    ///     <see cref="CopilotCli.FindInstalled()"/> finds. Its version is checked before it is used.
    /// </summary>
    InstalledCli = 0,

    /// <summary>
    ///     The runtime <c>Microsoft.Copilot.SDK</c> provisions into the application's output
    ///     (<c>runtimes/&lt;rid&gt;/native/copilot-runtime</c>), which the host provisions by referencing
    ///     the SDK package directly.
    /// </summary>
    Bundled = 1,
}
