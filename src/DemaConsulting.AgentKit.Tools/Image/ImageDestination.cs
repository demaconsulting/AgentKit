using System.Diagnostics.CodeAnalysis;
using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     Decides whether a destination a caller named may receive a new PNG file, and writes one
///     there when it may.
/// </summary>
/// <remarks>
///     <para>
///     <b>This helper exists because the destination taxonomy is security-relevant and must have
///     exactly one implementation.</b> Every tool in this family that produces an image file
///     answers the same five questions in the same order — is the name a <c>.png</c>, does the
///     policy's <em>write</em> decision permit it, is it a directory, does a file already exist
///     there, does its parent directory exist — and then writes with
///     <see cref="FileMode.CreateNew"/> so the existence guarantee has no window after it. A
///     second copy of that sequence would be a second place a later correction has to reach, and
///     the one it failed to reach would be the one an operator was relying on.
///     </para>
///     <para>
///     <b>The write decision is taken alone, never derived from the read that admitted the
///     source.</b> This is <see cref="TextFile.TextFileCreateTool"/>'s reasoning: a path an agent
///     may read is refused for creation unless a read-write grant permits it too, which is what
///     keeps a read-wide, write-narrow configuration meaningful rather than decorative. Deriving
///     the write from the read would silently convert every readable location into a writable
///     one, so one call exercises both of the grants an application configured rather than one of
///     them twice.
///     </para>
///     <para>
///     <b>The policy's own refusal is returned unchanged.</b> It enumerates every permitted
///     location with its access level, and that is what lets a confined agent recover to one it
///     may actually use. Every refusal this helper composes itself, by contrast, is fixed text
///     naming no host path, no permitted location and no directory separator.
///     </para>
///     <para>
///     <b>The refusal for a destination the operating system nonetheless rejects is left to the
///     caller.</b> It is the one message in the sequence that names <em>what</em> was being
///     written — a cropped region, a trimmed region — so it belongs to the tool that knows, in
///     the same way the inline byte-ceiling refusal does. <see cref="TryWriteAsync"/> therefore
///     reports the failure and composes nothing.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads.
///     </para>
/// </remarks>
internal static class ImageDestination
{
    /// <summary>
    ///     The refusal used when the destination does not name a <c>.png</c> file.
    /// </summary>
    /// <remarks>
    ///     A region is always encoded as PNG, so a destination named for another format would
    ///     hold PNG bytes under a name that says otherwise. The reason is
    ///     <see cref="DenialReason.InvalidRequest"/> rather than
    ///     <see cref="DenialReason.UnsupportedMediaType"/> because no file exists yet whose type
    ///     could be unsupported: the contradiction is in the request. A blank or whitespace-only
    ///     destination has no extension and so lands here too, which is deliberate — it is a
    ///     truthful and actionable answer, and it means a destination the model did not really
    ///     intend is never silently read as "no destination".
    /// </remarks>
    internal const string MustBePng =
        "The destination must name a .png file, because a region is always encoded as PNG. "
        + "Supply a destination path whose file name ends in '.png'.";

    /// <summary>
    ///     The refusal used when the destination names a directory rather than a file.
    /// </summary>
    /// <remarks>
    ///     Worded as <see cref="TextFile.TextFileCreateTool"/>'s is, so an agent meets one rule
    ///     for creating a file rather than one rule per family.
    /// </remarks>
    internal const string IsDirectory =
        "The destination path is a directory, not a file. Name the .png file to create within it.";

    /// <summary>
    ///     The refusal used when a file already exists at the destination.
    /// </summary>
    /// <remarks>
    ///     The same guarantee <see cref="TextFile.TextFileCreateTool"/> makes, for the same
    ///     reason: a figure silently replaced is a document that now points at a different
    ///     picture with nothing reporting an error. It also protects the source, because a
    ///     destination equal to the image being read names a file already proven to exist.
    /// </remarks>
    internal const string Exists =
        "A file already exists at the destination path. This tool writes a new file and does not "
        + "replace an existing one. Name a destination that does not exist yet.";

    /// <summary>
    ///     The refusal used when the destination's parent directory does not exist.
    /// </summary>
    /// <remarks>
    ///     A missing parent is refused rather than materialized, because silently creating a tree
    ///     is a side effect the operator never asked for and, on a mistyped path, would scatter
    ///     directories the agent then believes are real.
    /// </remarks>
    internal const string ParentMissing =
        "The parent directory of the destination path does not exist. This tool creates no "
        + "directory, so name a destination inside a directory that already exists.";

    /// <summary>
    ///     Determines whether a destination the caller named is named as a PNG file.
    /// </summary>
    /// <remarks>
    ///     Judged through the family's own extension map rather than by inspecting the string, so
    ///     "what is this file" keeps one answer across the family and a capitalized extension
    ///     names the same content. This is a request-shape question and needs no file system to
    ///     answer, which is why it is offered separately from <see cref="TryResolve"/> — a
    ///     contradiction in the request is refused before a policy decision is taken.
    /// </remarks>
    /// <param name="destination">The destination the caller named; may be blank.</param>
    /// <returns>
    ///     <see langword="true"/> when the destination names a <c>.png</c> file; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static bool NamesPng(string destination)
    {
        return ImageMediaTypes.TryResolveMediaType(destination, out var mediaType)
            && string.Equals(mediaType, ImageMediaTypes.Png, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Resolves a named destination through the policy's write decision and confirms the file
    ///     system can accept a new file there.
    /// </summary>
    /// <remarks>
    ///     The checks are the text file family's, in its order and its voice, so an agent meets
    ///     one rule for creating a file rather than one rule per family. The caller is expected
    ///     to have proven the source exists before calling this, which is what makes a
    ///     destination equal to the source fall into the already-exists refusal rather than
    ///     consuming the file being read.
    /// </remarks>
    /// <param name="policy">The access policy governing the write.</param>
    /// <param name="destination">The destination the caller named; never null.</param>
    /// <param name="resolved">
    ///     On success, the permitted destination together with the form a confirmation reports it
    ///     in; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denial">
    ///     On refusal, the composed refusal naming its reason; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when a new file may be written at the destination; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static bool TryResolve(
        PathPolicy policy,
        string destination,
        [NotNullWhen(true)] out Destination? resolved,
        [NotNullWhen(false)] out object? denial)
    {
        resolved = null;

        // The write decision alone, independent of the read that admitted the source.
        if (!policy.TryResolveWrite(destination, out var realDestination, out var denialMessage))
        {
            denial = ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
            return false;
        }

        // Writing a file over a directory is not a meaningful operation.
        if (Directory.Exists(realDestination))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, IsDirectory);
            return false;
        }

        // The defining guarantee: an existing file is never replaced. Because the source has
        // already been proven to exist by the time this runs, a request naming the source as its
        // own destination is refused here too, so an image can never be consumed by its own
        // region.
        if (System.IO.File.Exists(realDestination))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, Exists);
            return false;
        }

        // A missing parent is refused, never created.
        var parent = Path.GetDirectoryName(realDestination);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            denial = ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            return false;
        }

        // The confirmation reports the destination in the policy's own dialect: relative when
        // the anchor is granted and the result lies within it, absolute otherwise — which for a
        // session folder outside the workspace is the only truthful answer. Computed once, here,
        // so the write and the confirmation cannot disagree about what was written.
        resolved = new Destination(
            realDestination,
            policy.EmitRelative(realDestination, destination)
                ? Path.GetRelativePath(policy.WorkingDirectory, realDestination)
                : realDestination);
        denial = null;
        return true;
    }

    /// <summary>
    ///     Writes encoded bytes to a destination the policy has already permitted and the file
    ///     system has already been confirmed able to accept.
    /// </summary>
    /// <remarks>
    ///     <b><see cref="FileMode.CreateNew"/> is what makes "does not replace an existing one" a
    ///     guarantee rather than a check with a window after it.</b> The existence refusal
    ///     <see cref="TryResolve"/> takes is what <em>teaches</em> — without it the model would
    ///     receive only "could not be written" and have nothing to correct — and this is what
    ///     <em>enforces</em>: a file appearing between the two is refused rather than silently
    ///     destroyed. Neither is redundant.
    ///     <para>
    ///     <b>No exception's own message is ever surfaced.</b> The failure is reported as a plain
    ///     <see langword="false"/> and the caller states, in its own words, that what it was
    ///     writing could not be written — because this helper does not know why the operating
    ///     system refused, and a guess would be worse than silence.
    ///     </para>
    /// </remarks>
    /// <param name="bytes">The encoded bytes to write.</param>
    /// <param name="destination">The permitted destination and the form it is reported in.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>
    ///     <see langword="true"/> when the file was written; <see langword="false"/> when the
    ///     file system refused it.
    /// </returns>
    internal static async Task<bool> TryWriteAsync(
        byte[] bytes,
        Destination destination,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                destination.RealPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ImageAdmission.IsAccessFailure(exception))
        {
            // A permitted destination the operating system nonetheless refused. The reason is
            // not known here, and a guess would be worse than silence, so the caller states only
            // that what it was writing could not be written.
            return false;
        }

        return true;
    }

    /// <summary>
    ///     A destination the policy has permitted for writing, carried together with the form a
    ///     confirmation reports it in.
    /// </summary>
    /// <remarks>
    ///     Carried as one value so that no method has to take two same-typed string parameters a
    ///     caller could transpose, and so the write and the confirmation cannot disagree about
    ///     which path was meant. The reported form is computed once, at resolution, through
    ///     <see cref="PathPolicy.EmitRelative"/> — the dialect every other tool in the library
    ///     reports a path in.
    /// </remarks>
    /// <param name="RealPath">The real location the policy permitted the write to.</param>
    /// <param name="Reported">
    ///     The destination as a confirmation names it: relative to the working directory when the
    ///     policy's dialect calls for that, and absolute otherwise.
    /// </param>
    internal sealed record Destination(string RealPath, string Reported);
}
