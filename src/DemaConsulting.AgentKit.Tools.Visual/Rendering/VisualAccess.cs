using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Files;

namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     Maps an AgentKit access policy onto the file-access policy the rendering library applies
///     to every file a rendered document references, and chooses the folder those references are
///     resolved against.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why a mapping is needed at all.</b> A document names other files: an SVG embeds a
///     picture, a PlantUML diagram includes another, a Vega-Lite chart reads a data file, an
///     OpenSCAD model includes a library. The rendering library opens those itself, so AgentKit's
///     own read decision would never see them unless it is handed to the library as a policy the
///     library consults. The rendering library's default reads any local file, which is exactly
///     the escape a confined agent must not be given.
///     </para>
///     <para>
///     <b>Three parts, every one of which must allow a file.</b> First, <c>UnderRoots</c> over
///     the root of every grant: the path as written and the real path of the opened handle must
///     both lie inside a grant. The library captures the real location of each root when the
///     policy is created, so the policy is created for each render rather than once per
///     composition — a grant root created after the composition was built (a session folder) is
///     then honored rather than denied. The part is omitted when any grant is unrestricted,
///     because an unrestricted grant has no root to confine to. Second, <c>NoSymlinks</c>: a link
///     or junction below the folder the reference starts from is refused whatever its target.
///     Third, a delegate that asks <see cref="PathPolicy.TryResolveRead"/> about the file, so deny
///     patterns and AgentKit's own rules apply to every referenced file exactly as they apply to a
///     path a tool is given, and the final decision is AgentKit's.
///     </para>
///     <para>
///     <b>The delegate asks about the path as written, not the real path.</b> The access policy
///     resolves a path without following links, so the written full path is the dialect it
///     decides in. Asking about the real path instead would refuse every file whose real path
///     differs from its written path by a link the host placed above a grant — on macOS the
///     temporary folder is reached through one — while adding nothing: a real path outside the
///     grants is refused by <c>UnderRoots</c>, and a link below the starting folder by
///     <c>NoSymlinks</c>.
///     </para>
///     <para>
///     The library's <c>AllowAll</c> is never used. Network paths, device paths and alternate
///     data streams are refused by the library whatever the policy, and a platform that cannot
///     report the real path of an opened file (Linux without <c>/proc</c>) has every reference
///     refused.
///     </para>
///     <para>
///     <b>The delegate's refusal is fixed text.</b> The policy's own denial would name the
///     permitted locations and the full path of the file — an absolute host path the model never
///     supplied — so the delegate states only that the file is outside the locations the agent may
///     read. The library prefixes that with the reference as the document wrote it, which the
///     model supplied itself.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads; each policy it creates is immutable.
///     </para>
/// </remarks>
internal static class VisualAccess
{
    /// <summary>
    ///     The name the delegate part carries in the library's diagnostics.
    /// </summary>
    internal const string DelegateName = "agentkit-read";

    /// <summary>
    ///     The reason the delegate gives for a referenced file the access policy does not permit.
    /// </summary>
    /// <remarks>
    ///     A sentence fragment without a trailing period, as the library expects. It carries no
    ///     host detail.
    /// </remarks>
    internal const string OutsideReason = "the file is outside the locations the agent may read";

    /// <summary>
    ///     The hint the delegate gives for a referenced file the access policy does not permit.
    /// </summary>
    internal const string OutsideHint = "reference a file inside a location you may read";

    /// <summary>
    ///     The comparison two file system paths are equal under on this platform.
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    ///     Creates the file-access policy that confines a render's referenced files to what the
    ///     access policy permits the agent to read.
    /// </summary>
    /// <remarks>
    ///     Called for each render rather than once per composition; see the type remarks for why.
    /// </remarks>
    /// <param name="policy">The access policy the agent's tools observe. Must not be null.</param>
    /// <returns>The combined policy; never <c>AllowAll</c>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static FileAccessPolicy ToFileAccess(PathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var parts = new List<FileAccessPolicy>();

        // Confine both the written and the real path to the grant roots, unless an unrestricted
        // grant means there is no set of roots that describes what the agent may read.
        if (policy.Grants.Count > 0 && policy.Grants.All(grant => grant.Root is not null))
        {
            parts.Add(FileAccessPolicy.UnderRoots(policy.Grants.Select(grant => grant.Root!)));
        }

        // Refuse a link below the folder a reference starts from, whatever its target.
        parts.Add(FileAccessPolicy.NoSymlinks);

        // Ask AgentKit's own read decision, so deny patterns apply to referenced files too.
        parts.Add(FileAccessPolicy.FromDelegate(DelegateName, request => Check(policy, request)));

        return FileAccessPolicy.Combine([.. parts]);
    }

    /// <summary>
    ///     Chooses the folder an inline source's relative references are resolved against.
    /// </summary>
    /// <remarks>
    ///     Text supplied inline has no folder of its own. Its references are resolved against the
    ///     working directory, like every other path a model states, but only when the policy
    ///     permits reading it; otherwise no folder is given and every relative reference fails
    ///     closed.
    /// </remarks>
    /// <param name="policy">The access policy the agent's tools observe. Must not be null.</param>
    /// <returns>The working directory, or <see langword="null"/> when it is not readable.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static string? InlineBaseFolder(PathPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        // An omitted path denotes the working directory itself.
        return policy.TryResolveRead(null, out var workingDirectory, out _) ? workingDirectory : null;
    }

    /// <summary>
    ///     Decides one referenced file through the access policy's read decision.
    /// </summary>
    /// <param name="policy">The access policy the agent's tools observe.</param>
    /// <param name="request">The library's description of the opened file.</param>
    /// <returns>Allow when the policy permits reading the file; otherwise a fixed denial.</returns>
    private static FileAccessDecision Check(PathPolicy policy, FileAccessRequest request)
    {
        // The policy decides on the full path as written, the dialect it resolves paths in, and
        // its answer must name that same file; anything else is refused.
        return policy.TryResolveRead(request.RequestedPath, out var resolved, out _)
            && string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(request.RequestedPath), PathComparison)
            ? FileAccessDecision.Allow
            : FileAccessDecision.Deny(OutsideReason, OutsideHint);
    }
}
