using System.Diagnostics.CodeAnalysis;
using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     Decides whether a rendered picture may be saved where the caller named, and saves it there,
///     replacing an earlier rendering of the same name.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why replacing is right here, unlike the region tools.</b> The region tools refuse an
///     existing destination, because a figure silently replaced is a document now pointing at a
///     different picture. A rendering is different: the model draws, looks, fixes and draws again,
///     saving to the same name each time, and a refusal on the second save would stop that loop or
///     litter the workspace with a new name per attempt. The same write grant that lets
///     <c>text_file_write</c> replace a file's content governs it.
///     </para>
///     <para>
///     <b>The checks, in order.</b> The destination is read as the policy reads it, with
///     surrounding whitespace removed once, and that one value is used by every check, the write
///     and the reported form. The name must end in <c>.png</c>, because the bytes are always PNG
///     (<see cref="DenialReason.InvalidRequest"/>); the policy's write decision must
///     permit it, and its own refusal is returned unchanged so that it can name the writable
///     locations (<see cref="DenialReason.PathNotPermitted"/>); it must not name a directory
///     (<see cref="DenialReason.InvalidRequest"/>); and its parent directory must already exist,
///     because no directory is ever created (<see cref="DenialReason.TargetNotFound"/>). The write
///     decision is taken alone, never derived from a read.
///     </para>
///     <para>
///     <b>The replacement is atomic.</b> The bytes are written to a new temporary file in the same
///     directory, flushed, and moved over the target in one step, so a reader never sees a
///     half-written picture; the temporary file is removed on any failure.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads.
///     </para>
/// </remarks>
internal static class RenderDestination
{
    /// <summary>
    ///     The refusal used when the destination does not name a <c>.png</c> file.
    /// </summary>
    internal const string MustBePng =
        "The destination must name a .png file, because a rendering is always encoded as PNG.";

    /// <summary>
    ///     The refusal used when the destination names a directory.
    /// </summary>
    internal const string IsDirectory =
        "The destination path is a directory, not a file. Name the .png file to write within it.";

    /// <summary>
    ///     The refusal used when the destination's parent directory does not exist.
    /// </summary>
    internal const string ParentMissing =
        "The parent directory of the destination path does not exist. No directory is created, so "
        + "name a destination inside a directory that already exists.";

    /// <summary>
    ///     Resolves a named destination through the policy's write decision and confirms the file
    ///     system can take a file there.
    /// </summary>
    /// <param name="policy">The access policy governing the write. Must not be null.</param>
    /// <param name="destination">The destination the caller named. Must not be null.</param>
    /// <param name="resolved">On success, the destination and the form it is reported in.</param>
    /// <param name="denial">On refusal, the composed refusal.</param>
    /// <returns>True when a picture may be written to the destination.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static bool TryResolve(
        PathPolicy policy,
        string destination,
        [NotNullWhen(true)] out Destination? resolved,
        [NotNullWhen(false)] out object? denial)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(destination);

        resolved = null;

        // Read as the policy reads it: surrounding whitespace removed once, used everywhere.
        var requested = destination.Trim();

        // A request-shape contradiction is refused before any policy decision is taken.
        if (!requested.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, MustBePng);
            return false;
        }

        // The write decision alone; its refusal names the writable locations.
        if (!policy.TryResolveWrite(requested, out var realPath, out var denialMessage))
        {
            denial = ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
            return false;
        }

        if (Directory.Exists(realPath))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, IsDirectory);
            return false;
        }

        // A missing parent is refused, never created.
        var parent = Path.GetDirectoryName(realPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            denial = ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            return false;
        }

        // Reported in the policy's own dialect, computed once so the write and the confirmation
        // cannot disagree about which file was meant.
        resolved = new Destination(
            realPath,
            policy.EmitRelative(realPath, requested)
                ? Path.GetRelativePath(policy.WorkingDirectory, realPath)
                : realPath);
        denial = null;
        return true;
    }

    /// <summary>
    ///     Writes a picture to a resolved destination, replacing any file already there.
    /// </summary>
    /// <remarks>
    ///     No exception's own message is surfaced: a failure is reported as <see langword="false"/>
    ///     and the caller states that the picture could not be saved.
    /// </remarks>
    /// <param name="bytes">The encoded picture. Must not be null.</param>
    /// <param name="destination">The resolved destination. Must not be null.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>True when the picture was written; false when the file system refused it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static async Task<bool> TryWriteAsync(
        byte[] bytes,
        Destination destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(destination);

        var folder = Path.GetDirectoryName(destination.RealPath)!;
        var temporary = Path.Combine(
            folder,
            "." + Path.GetFileName(destination.RealPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            // A new file of its own, so nothing else is ever overwritten by the temporary write.
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // One step from the reader's point of view: the old picture or the new one.
            File.Move(temporary, destination.RealPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>
    ///     Removes a temporary file left by a failed write, ignoring a failure to do so.
    /// </summary>
    /// <param name="path">The temporary file.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temporary file costs disk space and nothing more.
        }
    }

    /// <summary>
    ///     A destination the policy permitted, with the form a confirmation reports it in.
    /// </summary>
    /// <param name="RealPath">The real location the policy permitted the write to.</param>
    /// <param name="Reported">The destination as a confirmation names it.</param>
    internal sealed record Destination(string RealPath, string Reported);
}
